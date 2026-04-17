using System;
using System.IO;
using System.Text;
using CodeDesigner.Ps2.ZdbArchive;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace CodeDesigner.Ps2.TestProject
{
    [TestClass]
    public class TestZdbArchive
    {
        private static string TempFile(string dir, string name, byte[] data)
        {
            string path = Path.Combine(dir, name);
            File.WriteAllBytes(path, data);
            return path;
        }

        [TestMethod]
        public void ZdbArchive_NewType1_EmptyFileCount()
        {
            var z = new ZDBArchive();
            z.NewArchive(1);
            Assert.AreEqual(1, z.GetArchiveType());
            Assert.AreEqual(0, z.GetFileCount());
        }

        [TestMethod]
        public void ZdbArchive_NewType2_EmptyFileCount()
        {
            var z = new ZDBArchive();
            z.NewArchive(2);
            Assert.AreEqual(2, z.GetArchiveType());
            Assert.AreEqual(0, z.GetFileCount());
        }

        [TestMethod]
        public void ZdbArchive_AddFile_NoArchiveOpen_ReturnsMinusOne()
        {
            var z = new ZDBArchive();
            Assert.AreEqual(-1, z.AddFile("/any/file", "z:/file", 0));
        }

        [TestMethod]
        public void ZdbArchive_AddFile_MissingSource_ReturnsMinusTwo()
        {
            var z = new ZDBArchive();
            z.NewArchive(1);
            Assert.AreEqual(-2, z.AddFile("/nonexistent/file.bin", "z:/file.bin", 0));
        }

        [TestMethod]
        public void ZdbArchive_AddFile_Type1_IncreasesCount()
        {
            string tmp = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString());
            Directory.CreateDirectory(tmp);
            try
            {
                var z = new ZDBArchive();
                z.NewArchive(1);
                Assert.AreEqual(0, z.AddFile(TempFile(tmp, "a.bin", new byte[] { 1, 2, 3 }), "z:/a.bin", 0));
                Assert.AreEqual(1, z.GetFileCount());
                Assert.AreEqual("a.bin", z.GetFileName(0));
            }
            finally { Directory.Delete(tmp, true); }
        }

        [TestMethod]
        public void ZdbArchive_AddFile_Type2_IncreasesCount()
        {
            string tmp = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString());
            Directory.CreateDirectory(tmp);
            try
            {
                var z = new ZDBArchive();
                z.NewArchive(2);
                Assert.AreEqual(0, z.AddFile(TempFile(tmp, "b.bin", new byte[] { 0xDE, 0xAD }), "z:/b.bin", 0));
                Assert.AreEqual(1, z.GetFileCount());
                Assert.AreEqual("b.bin", z.GetFileName(0));
            }
            finally { Directory.Delete(tmp, true); }
        }

        [TestMethod]
        public void ZdbArchive_GetFileBytes_MatchesSource()
        {
            string tmp = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString());
            Directory.CreateDirectory(tmp);
            try
            {
                byte[] payload = Encoding.ASCII.GetBytes("hello archive");
                var z = new ZDBArchive();
                z.NewArchive(1);
                z.AddFile(TempFile(tmp, "p.bin", payload), "z:/p.bin", 0);
                z.GetFileBytes(0, out byte[] result);
                CollectionAssert.AreEqual(payload, result);
            }
            finally { Directory.Delete(tmp, true); }
        }

        [TestMethod]
        public void ZdbArchive_SetFileBytes_UpdatesContent()
        {
            string tmp = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString());
            Directory.CreateDirectory(tmp);
            try
            {
                byte[] original = new byte[] { 1, 2, 3 };
                byte[] updated  = new byte[] { 0xAA, 0xBB };
                var z = new ZDBArchive();
                z.NewArchive(1);
                z.AddFile(TempFile(tmp, "u.bin", original), "z:/u.bin", 0);
                Assert.IsTrue(z.SetFileBytes(0, ref updated));
                z.GetFileBytes(0, out byte[] r);
                CollectionAssert.AreEqual(updated, r);
            }
            finally { Directory.Delete(tmp, true); }
        }

        [TestMethod]
        public void ZdbArchive_SaveAndOpen_Type1_Roundtrip()
        {
            string tmp = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString());
            Directory.CreateDirectory(tmp);
            try
            {
                byte[] payload = Encoding.ASCII.GetBytes("roundtrip");
                string archive = Path.Combine(tmp, "a.zdb");
                var z = new ZDBArchive();
                z.NewArchive(1);
                z.AddFile(TempFile(tmp, "rt.bin", payload), "z:/rt.bin", 0);
                Assert.IsTrue(z.SaveArchive(archive));

                var z2 = new ZDBArchive();
                Assert.AreEqual(1, z2.OpenArchive(archive));
                z2.GetFileBytes(0, out byte[] r);
                CollectionAssert.AreEqual(payload, r);
            }
            finally { Directory.Delete(tmp, true); }
        }

        [TestMethod]
        public void ZdbArchive_SaveAndOpen_Type2_Roundtrip()
        {
            string tmp = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString());
            Directory.CreateDirectory(tmp);
            try
            {
                byte[] payload = new byte[] { 0x10, 0x20, 0x30 };
                string archive = Path.Combine(tmp, "b.zdb");
                var z = new ZDBArchive();
                z.NewArchive(2);
                z.AddFile(TempFile(tmp, "t2.bin", payload), "z:/t2.bin", 0);
                Assert.IsTrue(z.SaveArchive(archive));

                var z2 = new ZDBArchive();
                Assert.AreEqual(1, z2.OpenArchive(archive));
                z2.GetFileBytes(0, out byte[] r);
                CollectionAssert.AreEqual(payload, r);
            }
            finally { Directory.Delete(tmp, true); }
        }

        [TestMethod]
        public void ZdbArchive_OpenArchive_InvalidData_ReturnsMinusOne()
        {
            string tmp = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString());
            Directory.CreateDirectory(tmp);
            try
            {
                string bad = TempFile(tmp, "bad.zdb", new byte[] { 0xFF, 0xFF, 0xFF, 0xFF });
                Assert.AreEqual(-1, new ZDBArchive().OpenArchive(bad));
            }
            finally { Directory.Delete(tmp, true); }
        }

        [TestMethod]
        public void ZdbArchive_OpenArchive_Missing_ReturnsMinusOne()
        {
            Assert.AreEqual(-1, new ZDBArchive().OpenArchive("/nonexistent/archive.zdb"));
        }

        [TestMethod]
        public void ZdbArchive_ExtractFile_WritesCorrectBytes()
        {
            string tmp = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString());
            Directory.CreateDirectory(tmp);
            try
            {
                byte[] payload = Encoding.ASCII.GetBytes("extract me");
                string archive = Path.Combine(tmp, "e.zdb");
                string dest    = Path.Combine(tmp, "out.bin");
                var z = new ZDBArchive();
                z.NewArchive(1);
                z.AddFile(TempFile(tmp, "e.bin", payload), "z:/e.bin", 0);
                z.SaveArchive(archive);

                var z2 = new ZDBArchive();
                z2.OpenArchive(archive);
                Assert.IsTrue(z2.ExtractFile(dest, 0));
                CollectionAssert.AreEqual(payload, File.ReadAllBytes(dest));
            }
            finally { Directory.Delete(tmp, true); }
        }

        [TestMethod]
        public void ZdbArchive_DeleteFile_DecreasesCount()
        {
            string tmp = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString());
            Directory.CreateDirectory(tmp);
            try
            {
                var z = new ZDBArchive();
                z.NewArchive(1);
                z.AddFile(TempFile(tmp, "d.bin", new byte[] { 1 }), "z:/d.bin", 0);
                Assert.AreEqual(1, z.GetFileCount());
                z.DeleteFile("z:/", "d.bin");
                Assert.AreEqual(0, z.GetFileCount());
            }
            finally { Directory.Delete(tmp, true); }
        }

        [TestMethod]
        public void ZdbArchive_GetDIRListing_ContainsFile()
        {
            string tmp = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString());
            Directory.CreateDirectory(tmp);
            try
            {
                var z = new ZDBArchive();
                z.NewArchive(1);
                z.AddFile(TempFile(tmp, "listed.bin", new byte[4]), "z:/listed.bin", 0);
                string listing = z.GetDIRListing("z:/");
                Assert.IsTrue(listing.Contains("listed.bin"));
                Assert.IsTrue(listing.Contains("[FILE]"));
            }
            finally { Directory.Delete(tmp, true); }
        }

        [TestMethod]
        public void ZdbArchive_FixAllChecksums_DoesNotThrow()
        {
            string tmp = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString());
            Directory.CreateDirectory(tmp);
            try
            {
                var z = new ZDBArchive();
                z.NewArchive(1);
                z.AddFile(TempFile(tmp, "cs.bin", new byte[] { 0xAB, 0xCD }), "z:/cs.bin", 0);
                z.FixAllChecksums();
                Assert.AreEqual(1, z.GetFileCount());
            }
            finally { Directory.Delete(tmp, true); }
        }
    }
}
