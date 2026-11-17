using System;

namespace OpenUtau.Core.Format {
    /// <summary>
    /// Thrown when a .ustx file's ustx_version is newer than this build supports.
    /// Callers may reload with <c>forceOpenNewer: true</c> after user confirmation.
    /// </summary>
    public class ProjectVersionTooNewException : Exception {
        public string FilePath { get; }
        public Version ProjectVersion { get; }
        public Version SupportedVersion { get; }

        public ProjectVersionTooNewException(string filePath, Version projectVersion, Version supportedVersion)
            : base($"Project file version {projectVersion} is newer than supported {supportedVersion}: {filePath}") {
            FilePath = filePath;
            ProjectVersion = projectVersion;
            SupportedVersion = supportedVersion;
        }
    }
}
