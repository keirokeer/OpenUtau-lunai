using System.IO;
using OpenUtau.Core;
using Xunit;

namespace OpenUtau.Test.Core.Util {
    public class PathManagerInstalledLayoutTest {
        [Fact]
        public void DetectsNsisInstalledTxt() {
            string root = Path.Combine(Path.GetTempPath(), "ou-inst-" + Path.GetRandomFileName());
            Directory.CreateDirectory(root);
            try {
                File.WriteAllText(Path.Combine(root, "installed.txt"), "yes");
                Assert.True(PathManager.IsWindowsInstalledLayout(root));
            } finally {
                Directory.Delete(root, recursive: true);
            }
        }

        [Fact]
        public void DetectsVelopackUpdateExeInParent() {
            string pack = Path.Combine(Path.GetTempPath(), "ou-vp-" + Path.GetRandomFileName());
            string current = Path.Combine(pack, "current");
            Directory.CreateDirectory(current);
            try {
                File.WriteAllText(Path.Combine(pack, "Update.exe"), "fake");
                // Velopack: exe lives in current\, Update.exe sits one level up.
                Assert.True(PathManager.IsWindowsInstalledLayout(current));
                Assert.True(PathManager.IsWindowsInstalledLayout(pack));
            } finally {
                Directory.Delete(pack, recursive: true);
            }
        }

        [Fact]
        public void PortableWithoutMarkersIsNotInstalled() {
            string root = Path.Combine(Path.GetTempPath(), "ou-port-" + Path.GetRandomFileName());
            Directory.CreateDirectory(root);
            try {
                Assert.False(PathManager.IsWindowsInstalledLayout(root));
            } finally {
                Directory.Delete(root, recursive: true);
            }
        }
    }
}
