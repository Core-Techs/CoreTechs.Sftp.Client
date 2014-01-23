using Renci.SshNet;
using Renci.SshNet.Common;
using System;
using System.Collections.Generic;
using System.Data.Common;
using System.IO;
using System.Linq;
using System.Runtime.Remoting.Metadata.W3cXsd2001;
using System.Text;
using System.Threading.Tasks;

namespace CoreTechs.Sftp.Client
{
    /// <summary>
    /// Wrapper class for 'Renci.SshNet' SFTP client.
    /// </summary>
    public static class Sftp
    {
        public static string Host { get; set; }
        public static string Username { get; set; }
        public static string Password { get; set; }
        public static string Hostkey { get; set; }
        public static string Port { get; set; }

        private static string ConnectionString
        {
            get
            {
                if (string.IsNullOrEmpty(Host) || string.IsNullOrEmpty(Username) || string.IsNullOrEmpty(Hostkey))
                    return string.Empty;

                int port = 0;
                if (int.TryParse(Port, out port) == false)
                {
                    port = 22;
                }

                var cxnStrBldr = new DbConnectionStringBuilder();
                cxnStrBldr.Add("host", Host);
                cxnStrBldr.Add("username", Username);
                cxnStrBldr.Add("password", Password);
                cxnStrBldr.Add("port", port.ToString());
                cxnStrBldr.Add("hostkey", Hostkey);
                    
                return cxnStrBldr.ToString();
            }
        }

        /// <summary>
        /// Upload a file to the SFTP server specified in the public properties.
        /// </summary>
        /// <param name="fileName">Name of file to upload.</param>
        /// <param name="overwrite">Overwrite the file on the SFTP server if it already exists ? (default=false)</param>
        /// <param name="remoteDirectoryPath">Target path on remote server. (default=null)</param>
        /// <remarks>Assumption: the public properties (Host, Username, Password and Hostkey) have been initialized before calling this method.</remarks>
        public static void UploadFile(string fileName, bool overwrite = false, string remoteDirectoryPath = null)
        {
            if (string.IsNullOrEmpty(ConnectionString)) throw new ArgumentException("You must intialize the static properties to use this method (e.g. 'Host', 'Username', etc.)");
            UploadFile(ConnectionString, fileName, overwrite, remoteDirectoryPath);
        }

        /// <summary>
        /// Upload file(s) to the SFTP server specified in the public properties.
        /// </summary>
        /// <param name="localPath">Path to upload from.</param>
        /// <param name="searchPattern">Wildcard pattern to use to find files (e.g., "*.txt")</param>
        /// <param name="overwrite">Overwrite the file on the SFTP server if it already exists ? (default=false)</param>
        /// <param name="remoteDirectoryPath">Target path on remote server. (default=null)</param>
        /// <remarks>Assumption: the public properties (Host, Username, Password and Hostkey) have been initialized before calling this method.</remarks>
        public static void UploadFiles(string localPath, string searchPattern, bool overwrite = false, string remoteDirectoryPath = null)
        {
            if (string.IsNullOrEmpty(ConnectionString)) throw new ArgumentException("You must intialize the static properties to use this method (e.g. 'Host', 'Username', etc.)");
            UploadFiles(ConnectionString, localPath, searchPattern, overwrite, remoteDirectoryPath);
        }

        /// <summary>
        /// Upload a file to the SFTP server specified in the connection string.
        /// </summary>
        /// <param name="sftpConnectionString">Connection string for a SFTP server.</param>
        /// <param name="fileName">Name of file to upload.</param>
        /// <param name="overwrite">Overwrite the file on the SFTP server if it already exists ? (default=false)</param>
        /// <param name="remoteDirectoryPath">Target path on remote server. (default=null)</param>
        /// <remarks>
        /// The SFTP connection string is in the form: "username=USERNAME;password=PASSWORD;host=HOSTNAME;hostkey=ssh-dss 1024 9b:3f:14:6a:6f:36:7f:b7:58:f4:e4:89:0d:3f:c2:26".
        /// </remarks>
        public static void UploadFile(string sftpConnectionString, string fileName, bool overwrite = false, string remoteDirectoryPath = null)
        {
            try
            {
                using (var sftp = sftpConnectionString.CreateSftpClient())
                {
                    sftp.Connect();
                    sftp.UploadFile(fileName, overwrite, remoteDirectoryPath);
                    sftp.Disconnect();
                }
            }
            catch (SshException ex)  // Wrap SSH exception and re-throw.  This allows the caller to be ignorant of Renci implementation details.
            {
                throw new Exception(ex.Message, ex);
            }
        }

        /// <summary>
        /// Upload file(s) to the SFTP server specified in the connection string.
        /// </summary>
        /// <param name="sftpConnectionString">Connection string for a SFTP server.</param>
        /// <param name="localPath">Path to upload from.</param>
        /// <param name="searchPattern">Wildcard pattern to use to find files (e.g., "*.txt")</param>
        /// <param name="overwrite">Overwrite the file on the SFTP server if it already exists ? (default=false)</param>
        /// <param name="remoteDirectoryPath">Target path on remote server. (default=null)</param>
        /// <remarks>
        /// The SFTP connection string is in the form: "username=USERNAME;password=PASSWORD;host=HOSTNAME;hostkey=ssh-dss 1024 9b:3f:14:6a:6f:36:7f:b7:58:f4:e4:89:0d:3f:c2:26".
        /// </remarks>
        public static void UploadFiles(string sftpConnectionString, string localPath, string searchPattern, bool overwrite = false, string remoteDirectoryPath = null)
        {
            try
            {
                using (var sftp = sftpConnectionString.CreateSftpClient())
                {
                    foreach (string fileName in Directory.GetFiles(localPath, searchPattern))
                    {
                        sftp.Connect();
                        sftp.UploadFile(fileName, overwrite, remoteDirectoryPath);
                        sftp.Disconnect();
                    }
                }
            }
            catch (SshException ex)  // Wrap SSH exception and re-throw.  This allows the caller to be ignorant of Renci implementation details.
            {
                throw new Exception(ex.Message, ex);
            }
        }

        internal static bool DoesRemoteFileExist(this SftpClient sftp, string path)
        {
            if (path == null) throw new ArgumentNullException("path");

            try
            {
                sftp.Get(path);
            }
            catch (SftpPathNotFoundException)
            {
                return false;
            }
            return true;
        }

        internal static void UploadFile(this SftpClient sftp, string fileName, bool overwrite = false, string remoteDirectoryPath = null)
        {
            UploadFile(sftp, new FileInfo(fileName), overwrite, remoteDirectoryPath);
        }

        internal static void UploadFile(this SftpClient sftp, FileInfo file, bool overwrite = false, string remoteDirectoryPath = null)
        {
            if (file == null) throw new ArgumentNullException("file");

            // auto-connect
            if (sftp.IsConnected == false)
                sftp.Connect();

            // create remote directory and return the new path
            var dest = CreateRemoteDirectory(sftp, file.Name, remoteDirectoryPath);

            // stream file to ftp directory
            using (var stream = file.OpenRead())
                sftp.UploadFile(stream, dest, overwrite);
        }

        private static string CreateRemoteDirectory(SftpClient sftp, string fileName, string remoteDirectoryPath)
        {
            if (!string.IsNullOrWhiteSpace(remoteDirectoryPath))
            {
                // auto-connect
                if (sftp.IsConnected == false)
                    sftp.Connect();

                var exists = sftp.DoesRemoteFileExist(remoteDirectoryPath);

                if (!exists)
                    sftp.CreateDirectory(remoteDirectoryPath);

                return Path.Combine(remoteDirectoryPath, fileName);
            }
            return fileName;
        }

        internal static SftpClient CreateSftpClient(this string connectionString)
        {
            if (connectionString == null) throw new ArgumentNullException("connectionString");

            // get parts of connection string
            var csb = new DbConnectionStringBuilder { ConnectionString = connectionString };
            var host = csb["host"] as string;
            var username = csb["username"] as string;
            var password = csb["password"] as string;
            var hostkey = csb["hostkey"] as string;
            var port = Attempt.Get(() => Convert.ToInt32(csb["port"]));

            // do this when connstring is bad
            Action<string> badConnString = detail =>
            {
                throw new FormatException(
                    string.Format(
                        "SFTP connection string invalid. {0} Example: username=joe;password=pdubz;host=localhost;hostkey=ssh-dss 1024 3b:1c:20:aa:27:00:83:4f:7a:49:9c:9f:e7:67:ab:03",
                        detail ?? ""));
            };

            // check required conn string params
            if (new[] { host, username, password, hostkey }.Any(string.IsNullOrWhiteSpace))
                badConnString("Missing required parameter.");

            // check format of host key
            var hostKeyParts =
                hostkey.Split().Where(x => !string.IsNullOrWhiteSpace(x)).Select(x => x.Trim().ToLower()).ToArray();

            // host key needs 3 parts: name, length, fingerprint
            if (hostKeyParts.Length != 3)
                badConnString(string.Format("Host key '{0}' is incorrectly formatted.", hostkey));

            // length neeeds to be an int
            int hostKeyLength;
            if (!int.TryParse(hostKeyParts[1], out hostKeyLength))
                badConnString(string.Format("Host key length '{0}' is not an integer.", hostKeyParts[1]));

            // finter print needs to be a byte array as hex string
            var hostKeyPrint =
                Attempt.Get(() => SoapHexBinary.Parse(string.Concat(hostKeyParts[2].Where(char.IsLetterOrDigit))).Value);
            if (!hostKeyPrint.Succeeded)
                badConnString(string.Format("Invalid host key fingerprint '{0}'.", hostKeyParts[2]));

            // using only pw auth for time being
            var pwAuthMethod = new PasswordAuthenticationMethod(username, password);
            var connInfo = port.Succeeded
                ? new ConnectionInfo(host, port.Value, username, pwAuthMethod)
                : new ConnectionInfo(host, username, pwAuthMethod);

            var sftpClient = new SftpClient(connInfo);

            // validate host key
            sftpClient.HostKeyReceived += (s, e) =>
            {
                e.CanTrust = hostKeyParts[0].Equals(e.HostKeyName.Trim(), StringComparison.OrdinalIgnoreCase)
                             && hostKeyLength == e.KeyLength
                             && hostKeyPrint.Value.SequenceEqual(e.FingerPrint);
            };

            return sftpClient;
        }
    }
}
