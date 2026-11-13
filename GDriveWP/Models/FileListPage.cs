using System.Collections.Generic;

namespace GDriveWP
{
    public sealed class FileListPage
    {
        public List<DriveFile> Files { get; set; }
        public string NextPageToken { get; set; }
    }
}
