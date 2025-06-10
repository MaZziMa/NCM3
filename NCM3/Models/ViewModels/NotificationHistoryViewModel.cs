using System;
using System.Collections.Generic;

namespace NCM3.Models.ViewModels
{
    public class NotificationLogEntry
    {
        public string Type { get; set; } = string.Empty;
        public string Router { get; set; } = string.Empty;
        public string Status { get; set; } = string.Empty;
        public DateTime Timestamp { get; set; }
        public bool Success { get; set; }
        public string Details { get; set; } = string.Empty;
        public string AdditionalInfo { get; set; } = string.Empty;
    }

    public class NotificationHistoryViewModel
    {
        public List<NotificationLogEntry> RecentNotifications { get; set; } = new List<NotificationLogEntry>();
        public int TotalCount { get; set; }
        public string CurrentFilter { get; set; } = "all";
        public int Page { get; set; } = 1;
        public int PageSize { get; set; } = 20;
        public int TotalPages => (int)Math.Ceiling(TotalCount / (double)PageSize);
    }
}
