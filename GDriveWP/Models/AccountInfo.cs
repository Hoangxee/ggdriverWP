namespace GDriveWP
{
    public sealed class AccountInfo
    {
        public string DisplayName { get; set; }
        public string Email { get; set; }
        public string PhotoLink { get; set; }
        public long QuotaLimit { get; set; }    // 0 = unlimited
        public long QuotaUsage { get; set; }
        public long QuotaUsageInDrive { get; set; }
        public long QuotaUsageInTrash { get; set; }
    }
}
