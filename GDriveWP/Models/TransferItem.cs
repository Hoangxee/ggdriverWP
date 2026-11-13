using System.ComponentModel;
using System.Threading;
using Windows.UI.Xaml;

namespace GDriveWP
{
    public enum TransferKind { Upload, Download }

    /// <summary>One running / finished upload or download shown in the activity panel.</summary>
    public sealed class TransferItem : INotifyPropertyChanged
    {
        public TransferKind Kind { get; set; }
        public string Name { get; set; }
        public long Total { get; set; }
        public CancellationTokenSource Cts { get; set; }

        private long _done;
        public long Done
        {
            get { return _done; }
            set { _done = value; Raise("Done"); Raise("Percent"); Raise("StatusText"); }
        }

        private string _status;    // null while running, else final text
        public string Status
        {
            get { return _status; }
            set { _status = value; Raise("Status"); Raise("StatusText"); Raise("RunningVisibility"); }
        }

        public bool IsRunning { get { return _status == null; } }
        public Visibility RunningVisibility { get { return IsRunning ? Visibility.Visible : Visibility.Collapsed; } }
        public double Percent { get { return Total > 0 ? 100.0 * _done / Total : 0; } }
        public string KindText { get { return Kind == TransferKind.Upload ? "Đang tải lên" : "Đang tải xuống"; } }

        public string StatusText
        {
            get
            {
                if (_status != null) return _status;
                if (Total > 0) return KindText + " • " + Services.FileTypeInfo.FormatSize(_done) + " / " + Services.FileTypeInfo.FormatSize(Total);
                return KindText + " • " + Services.FileTypeInfo.FormatSize(_done);
            }
        }

        public event PropertyChangedEventHandler PropertyChanged;
        private void Raise(string name)
        {
            var h = PropertyChanged;
            if (h != null) h(this, new PropertyChangedEventArgs(name));
        }
    }
}
