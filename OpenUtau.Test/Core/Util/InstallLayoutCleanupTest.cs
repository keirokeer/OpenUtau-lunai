using System;
using System.IO;
using OpenUtau.Core.Util;
using Xunit;

namespace OpenUtau.Test.Core.Util {
    public class InstallLayoutCleanupTest {
        [Fact]
        public void QuarantinesNestedBinaryShadow() {
            string root = Path.Combine(Path.GetTempPath(), "ou-cleanup-" + Guid.NewGuid().ToString("N"));
            string nested = Path.Combine(root, "OpenUtau");
            Directory.CreateDirectory(nested);
            try {
                File.WriteAllText(Path.Combine(root, "OpenUtau-Lunai.exe"), "host");
                File.WriteAllText(Path.Combine(root, "installed.txt"), "yes");
                File.WriteAllText(Path.Combine(nested, "OpenUtau.Core.dll"), "core");
                File.WriteAllText(Path.Combine(nested, "OpenUtau.exe"), "old");

                InstallLayoutCleanup.Run(root);

                Assert.False(Directory.Exists(nested));
                Assert.NotEmpty(Directory.GetDirectories(root, "OpenUtau.bak-stale-*"));
            } finally {
                if (Directory.Exists(root)) {
                    Directory.Delete(root, recursive: true);
                }
            }
        }

        [Fact]
        public void LeavesNonShadowOpenUtauFolderAlone() {
            string root = Path.Combine(Path.GetTempPath(), "ou-cleanup-" + Guid.NewGuid().ToString("N"));
            string nested = Path.Combine(root, "OpenUtau");
            Directory.CreateDirectory(nested);
            try {
                File.WriteAllText(Path.Combine(root, "OpenUtau-Lunai.exe"), "host");
                // No OpenUtau.Core.dll → not a binary shadow
                File.WriteAllText(Path.Combine(nested, "notes.txt"), "user stuff");

                InstallLayoutCleanup.Run(root);

                Assert.True(Directory.Exists(nested));
                Assert.Empty(Directory.GetDirectories(root, "OpenUtau.bak-stale-*"));
            } finally {
                if (Directory.Exists(root)) {
                    Directory.Delete(root, recursive: true);
                }
            }
        }
    }
}
