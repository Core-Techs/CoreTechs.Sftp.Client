using NUnit.Framework;
using System;
using System.Data.Common;
using System.IO;

namespace CoreTechs.Sftp.Client.Tests
{
    class SFTPTests
    {
        private const string Cs =
            "username=Admin;password=Test@1234;host=127.0.0.1;port=22;hostkey=ssh-rsa 2048 bf:68:ae:99:b9:a8:08:34:71:5c:d1:4f:7d:0f:4b:c9";
        private const string DummyFileExt =
            ".test";
        private const string SearchPattern =
            "*" + DummyFileExt;

        [Test]
        public void CanMakeAndDeleteADummyFile()
        {
            const int mb = 1048576;
            const int twentyMb = mb * 20;
            FileInfo theFile;
            using (var dummy = new DummyFile(twentyMb))
            {
                theFile = dummy.FileInfo;
                Assert.That(theFile.Exists, Is.True);
                Assert.That(theFile.Length, Is.EqualTo(twentyMb));
            }
            theFile.Refresh();
            Assert.That(theFile.Exists, Is.False);
        }

        [Test]
        public void CanUploadToRootDir()
        {
            using (var file = new DummyFile(1024 * 1024 * 3))
            {
                Sftp.UploadFile(Cs, file.FileInfo.FullName, true);
            }
        }
        [Test]
        public void CanUpload2FilesToRootDir()
        {
            using (var file1 = new DummyFile(1024 * 1024 * 3))
            using (var file2 = new DummyFile(1024 * 1024 * 3))
            {
                Sftp.UploadFiles(Cs, file1.FileInfo.DirectoryName, SearchPattern, true);
            }
        }
        [Test]
        public void CanUploadFileToRootDirUsingProperties()
        {
            using (var file = new DummyFile(1024 * 1024 * 3))
            {
                var csb = new DbConnectionStringBuilder { ConnectionString = Cs };
                Sftp.Host = csb["host"] as string;
                Sftp.Username = csb["username"] as string;
                Sftp.Password = csb["password"] as string;
                Sftp.Hostkey = csb["hostkey"] as string;
                Sftp.Port = csb.ContainsKey("port") ? csb["port"] as string : string.Empty;

                Sftp.UploadFile(file.FileInfo.FullName, true);
            }
        }
        [Test]
        public void CanUpload2FilesToRootDirUsingProperties()
        {
            using (var file1 = new DummyFile(1024 * 1024 * 3))
            using (var file2 = new DummyFile(1024 * 1024 * 3))
            {
                var csb = new DbConnectionStringBuilder { ConnectionString = Cs };
                Sftp.Host = csb["host"] as string;
                Sftp.Username = csb["username"] as string;
                Sftp.Password = csb["password"] as string;
                Sftp.Hostkey = csb["hostkey"] as string;
                Sftp.Port = csb.ContainsKey("port") ? csb["port"] as string : string.Empty;

                Sftp.UploadFiles(file1.FileInfo.DirectoryName, SearchPattern, true);
            }
        }

        [Test]
        public void CanUploadToSubDir()
        {
            using (var file = new DummyFile(1024 * 1024 * 3))
            {
                Sftp.UploadFile(Cs, file.FileInfo.FullName, true, "subdir");
            }
        }

        [Test]
        public void CanUploadToSubSubDir()
        {
            using (var file = new DummyFile(1024 * 1024 * 3))
            {
                Sftp.UploadFile(Cs, file.FileInfo.FullName, true, "a/b/c");
            }
        }

        internal class DummyFile : IDisposable
        {
            public FileInfo FileInfo { get; set; }

            public DummyFile(int byteLength)
            {
                FileInfo = new FileInfo(Path.Combine(Path.GetDirectoryName(Path.GetTempFileName()), Guid.NewGuid().ToString() + DummyFileExt));
                File.WriteAllBytes(FileInfo.FullName, new byte[byteLength]);
            }

            public void Dispose()
            {
                try
                {
                    FileInfo.Delete();
                }
                catch { }
            }
        }
    }
}
