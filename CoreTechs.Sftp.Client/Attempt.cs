﻿using System;
using System.Collections;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace CoreTechs.Sftp.Client
{

    /// <summary>
    /// Utility and extension methods for convenient and easy to read error handling.
    /// </summary>
    internal class Attempt
    {
        /// <summary>
        /// Invokes the action, suppressing any thrown exception.
        /// </summary>
        /// <returns>The result of the invoked action.</returns>
        public static Attempt Do(Action action)
        {
            var begin = DateTimeOffset.Now;
            try
            {
                action();
                return new Attempt(begin);
            }
            catch (Exception ex)
            {
                return new Attempt(begin, ex);
            }
        }

        public static Task<Attempt> DoAsync(Action action)
        {
            return Task.Run(() => Do(action));
        }

        /// <summary>
        /// Invokes the factory, suppressing any thrown exception.
        /// </summary>
        /// <param name="default">The result value when not successful.</param>
        /// <returns></returns>
        public static Attempt<T> Get<T>(Func<T> factory, T @default = default(T))
        {
            var begin = DateTimeOffset.Now;
            try
            {
                return new Attempt<T>(begin, factory());
            }
            catch (Exception ex)
            {
                return new Attempt<T>(begin, @default, ex);
            }
        }

        public static Task<Attempt<T>> GetAsync<T>(Func<T> factory, T @default = default(T))
        {
            return Task.Run(() => Get(factory, @default));
        }


        private readonly Lazy<TimeSpan> _lazyDuration;

        /// <summary>
        /// True if an exception was not thrown; false otherwise.
        /// </summary>
        public bool Succeeded
        {
            get { return Exception == null; }
        }

        /// <summary>
        /// The exception that was thrown.
        /// </summary>
        public Exception Exception { get; private set; }

        /// <summary>
        /// When the attempt began.
        /// </summary>
        public DateTimeOffset BeginDateTime { get; private set; }

        /// <summary>
        /// When the attempt ended.
        /// </summary>
        public DateTimeOffset EndDateTime { get; private set; }

        /// <summary>
        /// How long the attempt took.
        /// </summary>
        public TimeSpan Duration
        {
            get
            {
                return _lazyDuration.Value;
            }
        }

        public Attempt(DateTimeOffset beginDateTime, Exception exception = null)
        {
            _lazyDuration = new Lazy<TimeSpan>(() => EndDateTime - BeginDateTime);
            EndDateTime = DateTimeOffset.Now;
            BeginDateTime = beginDateTime;
            Exception = exception;
        }

        public static class Repeatedly
        {
            /// <summary>
            /// Repeatedly yields a lazy invocation attempt of the action as an enumerable.
            /// </summary>
            public static IEnumerable<Lazy<Attempt>> Do(Action action)
            {
                while (true) yield return new Lazy<Attempt>(() => Attempt.Do(action));
            }

            /// <summary>
            /// Repeatedly yields a lazy invocation attempt of the factory as an enumerable.
            /// </summary>
            /// <param name="default">The result value when not successful.</param>
            public static IEnumerable<Lazy<Attempt<T>>> Get<T>(Func<T> factory, T @default = default(T))
            {
                while (true) yield return new Lazy<Attempt<T>>(() => Attempt.Get(factory, @default));
            }
        }
    }

    internal class Attempt<T> : Attempt
    {
        /// <summary>
        /// The value that was created by the factory.
        /// </summary>
        public T Value { get; private set; }

        public Attempt(DateTimeOffset beginDateTime, T value, Exception exception = null)
            : base(beginDateTime, exception)
        {
            Value = value;
        }
    }

    internal class Attempts<T> : IEnumerable<T> where T : Attempt
    {
        private readonly LinkedList<T> _attempts = new LinkedList<T>();

        /// <summary>
        /// The succeeding attempt. Will be null if all attempts failed.
        /// </summary>
        public T Success
        {
            get { return Succeeded ? _attempts.Last.Value : null; }
        }

        /// <summary>
        /// When attempts began.
        /// </summary>
        public DateTimeOffset BeginDateTime { get; private set; }

        /// <summary>
        /// When attempts completed.
        /// </summary>
        public DateTimeOffset EndDateTime { get; set; }

        /// <summary>
        /// Duration of all attempts.
        /// </summary>
        public TimeSpan Duration
        {
            get { return EndDateTime - BeginDateTime; }
        }

        /// <summary>
        /// Total number of attempts.
        /// </summary>
        public int AttemptCount { get; private set; }

        /// <summary>
        /// The number of most recent attempts to retain.
        /// </summary>
        public int? Capacity { get; set; }

        /// <summary>
        /// True if an attempt succeeded; false otherwise.
        /// </summary>
        public bool Succeeded
        {
            get
            {
                return _attempts.Count > 0 && _attempts.Last.Value.Succeeded;
            }
        }

        public Attempts()
        {
            BeginDateTime = DateTimeOffset.Now;
        }

        internal void Add(T attempt)
        {
            AttemptCount++;
            _attempts.AddLast(attempt);
            if (_attempts.Count > Capacity)
                _attempts.RemoveFirst();
        }

        public IEnumerator<T> GetEnumerator()
        {
            return _attempts.GetEnumerator();
        }

        IEnumerator IEnumerable.GetEnumerator()
        {
            return GetEnumerator();
        }

        public RepeatedFailureException<T> BuildException(string message = null)
        {
            return new RepeatedFailureException<T>(message, this);
        }
    }

    internal static class AttemptExtensions
    {
        /// <summary>
        /// Delays iteration when the predicate is satisfied.
        /// </summary>                  
        /// <param name="delayAdjustment">A function that can alter the delay between iterations.</param>        
        public static IEnumerable<T> DelayWhere<T>(this IEnumerable<T> source, Func<T, bool> predicate, TimeSpan delay,
            Func<TimeSpan, TimeSpan> delayAdjustment = null,
            CancellationToken cancellationToken = default(CancellationToken))
        {
            source = source.JoinAction((prev, next) =>
            {
                if (predicate(prev) && delay > TimeSpan.Zero)
                    // ReSharper disable once MethodSupportsCancellation
                    Task.Delay(delay, cancellationToken).Wait();

                if (delayAdjustment != null)
                    delay = delayAdjustment(delay);
            });

            foreach (var item in source)
            {
                yield return item;

                if (cancellationToken.IsCancellationRequested)
                    yield break;
            }
        }

        /// <summary>
        /// Delays further attempts after a failure.
        /// </summary>
        /// <param name="delayAdjustment">A function that can alter the delay between failed attempts.</param>        
        public static IEnumerable<Lazy<T>> DelayWhereFailed<T>(this IEnumerable<Lazy<T>> lazyAttempts, TimeSpan delay,
            Func<TimeSpan, TimeSpan> delayAdjustment = null,
            CancellationToken cancellationToken = default(CancellationToken)) where T : Attempt
        {
            return lazyAttempts.DelayWhere(x => !x.Value.Succeeded, delay, delayAdjustment, cancellationToken);
        }

        /// <summary>
        /// Delays further attempts after a failure.
        /// </summary>
        /// <param name="delayAdjustment">A function that can alter the delay between failed attempts.</param>
        public static IEnumerable<Lazy<T>> DelayWhereFailed<T>(this IEnumerable<Lazy<T>> lazyAttempts,
            double milliseconds, Func<double, double> delayAdjustment = null,
            CancellationToken cancellationToken = default(CancellationToken)) where T : Attempt
        {
            return lazyAttempts.DelayWhere(x => !x.Value.Succeeded, TimeSpan.FromMilliseconds(milliseconds),
                delayAdjustment != null
                    ? new Func<TimeSpan, TimeSpan>(x => TimeSpan.FromMilliseconds(delayAdjustment(x.TotalMilliseconds)))
                    : null,
                cancellationToken);
        }

        /// <summary>
        /// Attempts the operation until success or no further attempts remain, in which case an exception will be thrown.
        /// </summary>
        /// <param name="lazyAttempts">The operation attempts.</param>
        /// <param name="message">The message used when throwing a <see cref="RepeatedFailureException"/> after all attempts have failed.</param>
        /// <param name="maxRetainedAttempts">The max number of attempts to return on success or to include when throwing a <see cref="RepeatedFailureException"/> after all attempts have failed.</param>
        /// <exception cref="RepeatedFailureException" />
        public static Attempts<T> ThrowIfCantSucceed<T>(this IEnumerable<Lazy<T>> lazyAttempts, string message = null,
            int? maxRetainedAttempts = null) where T : Attempt
        {
            var attempts = lazyAttempts.Execute(maxRetainedAttempts);
            if (!attempts.Succeeded) throw attempts.BuildException(message);
            return attempts;
        }

        public static Task<Attempts<T>> ThrowIfCantSucceedAsync<T>(this IEnumerable<Lazy<T>> lazyAttempts,
            string message = null, int? maxRetainedAttempts = null)
            where T : Attempt
        {
            return Task.Run(() => lazyAttempts.ThrowIfCantSucceed(message, maxRetainedAttempts));
        }

        /// <summary>
        /// Gets the value of the first successful attempt or the default value.
        /// </summary>
        public static T GetValueOrDefault<T>(this IEnumerable<Attempt<T>> attempts)
        {
            var value = default(T);
            foreach (var attempt in attempts)
            {
                value = attempt.Value;
                if (attempt.Succeeded) break;
            }
            return value;
        }

        /// <summary>
        /// Asynchronous version of GetValueOrDefault.
        /// </summary>
        public static Task<T> GetValueOrDefaultAsync<T>(this IEnumerable<Attempt<T>> attempts)
        {
            return Task.Run(() => attempts.GetValueOrDefault());
        }

        /// <summary>
        /// Gets the value of the first successful attempt or the default value.
        /// </summary>
        public static T GetValueOrDefault<T>(this IEnumerable<Lazy<Attempt<T>>> lazyAttempts)
        {
            return lazyAttempts.Select(x => x.Value).GetValueOrDefault();
        }

        /// <summary>
        /// Asynchronous version of GetValueOrDefault.
        /// </summary>
        public static Task<T> GetValueOrDefaultAsync<T>(this IEnumerable<Lazy<Attempt<T>>> lazyAttempts)
        {
            return Task.Run(() => lazyAttempts.GetValueOrDefault());
        }

        /// <summary>
        /// Asynchronous version of Execute.
        /// </summary>
        /// <param name="maxReturnAttempts">The maximum number of attempts to return. When specified only the most recent attempts will be returned.</param>
        /// <returns>An array of the attempts.</returns>
        public static Task<Attempts<T>> ExecuteAsync<T>(this IEnumerable<Lazy<T>> lazyAttempts,
            int? maxReturnAttempts = null) where T : Attempt
        {
            return Task.Run(() => lazyAttempts.Execute(maxReturnAttempts));
        }

        /// <summary>
        /// Invokes lazy attempts until success or all attempts fail.
        /// </summary>
        /// <param name="maxReturnAttempts">The maximum number of attempts to return. When specified only the most recent attempts will be returned.</param>
        /// <returns>An array of the attempts.</returns>
        public static Attempts<T> Execute<T>(this IEnumerable<Lazy<T>> lazyAttempts, int? maxReturnAttempts = null)
            where T : Attempt
        {
            var attempts = new Attempts<T> { Capacity = maxReturnAttempts };
            foreach (var attempt in lazyAttempts.Select(x => x.Value))
            {
                attempts.Add(attempt);
                if (attempt.Succeeded) break;
            }
            attempts.EndDateTime = DateTimeOffset.Now;
            return attempts;
        }

        /// <summary>
        /// Invokes an action for each element that satisfies a condition.
        /// </summary>
        public static IEnumerable<T> When<T>(this IEnumerable<T> source, Func<T, bool> predicate, Action<T> action)
        {
            return source.Select(x =>
            {
                if (predicate(x))
                    action(x);
                return x;
            });
        }

        /// <summary>
        /// Causes iteration to halt after the specified time period has elapsed.
        /// </summary>
        public static IEnumerable<T> TakeForDuration<T>(this IEnumerable<T> source, TimeSpan duration,
            CancellationToken cancellationToken = default (CancellationToken))
        {
            var sw = new Lazy<Stopwatch>(Stopwatch.StartNew);
            return source.TakeWhile(x => !cancellationToken.IsCancellationRequested && sw.Value.Elapsed <= duration);
        }

        /// <summary>
        /// Causes iteration to halt after the specified time period has elapsed.
        /// </summary>
        public static IEnumerable<T> TakeForDuration<T>(this IEnumerable<T> source, double milliseconds,
            CancellationToken cancellationToken = default (CancellationToken))
        {
            return source.TakeForDuration(TimeSpan.FromMilliseconds(milliseconds), cancellationToken);
        }

        /// <summary>
        /// Causes failed attempts that satisfy the predicate to throw their exception, halting further attempts.
        /// </summary>
        public static IEnumerable<Lazy<T>> ThrowWhere<T>(this IEnumerable<Lazy<T>> lazyAttempts, Func<T, bool> predicate)
            where T : Attempt
        {
            return lazyAttempts.When(x => !x.Value.Succeeded && predicate(x.Value), x => { throw x.Value.Exception; });
        }

        /// <summary>
        /// Causes failed attempts that do not satisfy the predicate to throw their exception, halting further attempts.
        /// </summary>
        public static IEnumerable<Lazy<T>> CatchWhere<T>(this IEnumerable<Lazy<T>> attempts, Func<T, bool> predicate)
            where T : Attempt
        {
            return attempts.ThrowWhere(x => !predicate(x));
        }

        /// <summary>
        /// Modifies the attempts enumerable using the specified <see cref="IRetryStrategy"/>.
        /// </summary>
        public static IEnumerable<Lazy<T>> UsingStrategy<T>(this IEnumerable<Lazy<T>> lazyAttempts,
            IRetryStrategy strategy, CancellationToken cancellationToken) where T : Attempt
        {
            if (strategy == null) throw new ArgumentNullException("strategy");

            if (strategy.AttemptLimit > 0)
                lazyAttempts = lazyAttempts.Take(strategy.AttemptLimit.Value);

            if (strategy.MaxDuration > TimeSpan.Zero)
                lazyAttempts = lazyAttempts.TakeForDuration(strategy.MaxDuration.Value, cancellationToken);

            if (strategy.FailureDelay > TimeSpan.Zero || strategy.FailureDelayAdjustment != null)
                lazyAttempts = lazyAttempts.DelayWhereFailed(strategy.FailureDelay, strategy.FailureDelayAdjustment,
                    cancellationToken);

            if (strategy.ThrowPredicate != null)
                lazyAttempts = lazyAttempts.ThrowWhere(a => strategy.ThrowPredicate(a.Exception));

            if (strategy.CatchPredicate != null)
                lazyAttempts = lazyAttempts.CatchWhere(a => strategy.CatchPredicate(a.Exception));

            return lazyAttempts;
        }

        /// <summary>
        /// Causes the action to be invoked between each element of the enumerable.
        /// The action accepts the previous and next elements as it's arguments.
        /// </summary>
        public static IEnumerable<T> JoinAction<T>(this IEnumerable<T> source, Action<T, T> action)
        {
            var first = true;
            var prev = default(T);
            return source.Select(item =>
            {
                if (first) first = false;
                else action(prev, item);
                prev = item;
                return item;
            });
        }

        /// <summary>
        /// Repeatedly yields a lazy invocation attempt of the factory as an enumerable.
        /// </summary>
        /// <param name="default">The result value when not successful.</param>
        public static IEnumerable<Lazy<Attempt<T>>> Get<T>(this IRetryStrategy retryStrategy, Func<T> factory,
            T @default = default(T), CancellationToken cancellationToken = default(CancellationToken))
        {
            return Attempt.Repeatedly.Get(factory, @default).UsingStrategy(retryStrategy, cancellationToken);
        }

        /// <summary>
        /// Repeatedly yields a lazy invocation attempt of the action as an enumerable.
        /// </summary>
        public static IEnumerable<Lazy<Attempt>> Do(this IRetryStrategy retryStrategy, Action action,
            CancellationToken cancellationToken = default(CancellationToken))
        {
            return Attempt.Repeatedly.Do(action).UsingStrategy(retryStrategy, cancellationToken);
        }
    }


    public interface IRetryStrategy
    {
        /// <summary>
        /// The delay between attempts.
        /// </summary>
        TimeSpan FailureDelay { get; set; }

        /// <summary>
        /// The max number of attempts.
        /// </summary>
        int? AttemptLimit { get; set; }

        /// <summary>
        /// The max total duration for all attempts.
        /// </summary>
        TimeSpan? MaxDuration { get; set; }

        /// <summary>
        /// A predicate that when satisified will cause failed attempts to throw their exception, halting further attempts.
        /// </summary>
        Func<Exception, bool> ThrowPredicate { get; set; }

        /// <summary>
        /// A predicate that when not satisified will cause failed attempts to throw their exception, halting further attempts.
        /// </summary>
        Func<Exception, bool> CatchPredicate { get; set; }

        /// <summary>
        /// A function that can alter the delay between failed attempts.
        /// </summary>
        Func<TimeSpan, TimeSpan> FailureDelayAdjustment { get; set; }
    }

    internal class RetryStrategy : IRetryStrategy
    {
        /// <summary>
        /// The delay between attempts.
        /// </summary>
        public TimeSpan FailureDelay { get; set; }

        /// <summary>
        /// The max number of attempts.
        /// </summary>
        public int? AttemptLimit { get; set; }

        /// <summary>
        /// The max total duration for all attempts.
        /// </summary>
        public TimeSpan? MaxDuration { get; set; }

        /// <summary>
        /// A predicate that when satisified will cause failed attempts to throw their exception, halting further attempts.
        /// </summary>
        public Func<Exception, bool> ThrowPredicate { get; set; }

        /// <summary>
        /// A predicate that when not satisified will cause failed attempts to throw their exception, halting further attempts.
        /// </summary>
        public Func<Exception, bool> CatchPredicate { get; set; }

        /// <summary>
        /// A function that can alter the delay between failed attempts.
        /// </summary>
        public Func<TimeSpan, TimeSpan> FailureDelayAdjustment { get; set; }
    }

    internal abstract class RepeatedFailureException : AggregateException
    {
        protected RepeatedFailureException(string message, IEnumerable<Exception> exceptions)
            : base(message, exceptions) { }
    }

    internal sealed class RepeatedFailureException<T> : RepeatedFailureException where T : Attempt
    {
        public Attempts<T> Attempts { get; set; }

        public RepeatedFailureException(string message, Attempts<T> attempts)
            : base(message, GetExceptions(attempts))
        {
            Data["Attempts.AttemptCount"] = attempts.AttemptCount;
            Data["Attempts.BeginDateTime"] = attempts.BeginDateTime;
            Data["Attempts.EndDateTime"] = attempts.EndDateTime;
            Data["Attempts.Duration"] = attempts.Duration;
            Attempts = attempts;
        }

        private static IEnumerable<Exception> GetExceptions(IEnumerable<T> attempts)
        {
            return attempts
                .Where(x => !x.Succeeded)
                .Select(x =>
                {
                    Attempt.Do(() =>
                    {
                        x.Exception.Data["Attempt.BeginDateTime"] = x.BeginDateTime;
                        x.Exception.Data["Attempt.EndDateTime"] = x.EndDateTime;
                        x.Exception.Data["Attempt.Duration"] = x.Duration;
                    });
                    return x.Exception;
                });
        }
    }
}
