/**
 * Dashboard.js - Client-side functionality for the NCM3 dashboard
 */

// Function to format dates in a user-friendly way
function formatTimeAgo(dateString) {
    const date = new Date(dateString);
    const now = new Date();
    const diffMs = now - date;
    const diffSecs = Math.floor(diffMs / 1000);
    const diffMins = Math.floor(diffSecs / 60);
    const diffHours = Math.floor(diffMins / 60);
    const diffDays = Math.floor(diffHours / 24);

    if (diffDays > 0) {
        return diffDays + (diffDays === 1 ? ' day ago' : ' days ago');
    } else if (diffHours > 0) {
        return diffHours + (diffHours === 1 ? ' hour ago' : ' hours ago');
    } else if (diffMins > 0) {
        return diffMins + (diffMins === 1 ? ' minute ago' : ' minutes ago');
    } else {
        return 'just now';
    }
}

// Update progress bars with appropriate colors
function updateProgressBars() {
    document.querySelectorAll('.progress-bar').forEach(bar => {
        const value = parseInt(bar.getAttribute('aria-valuenow'));
        if (value >= 90) {
            bar.classList.add('bg-success');
            bar.classList.remove('bg-warning', 'bg-danger');
        } else if (value >= 60) {
            bar.classList.add('bg-warning');
            bar.classList.remove('bg-success', 'bg-danger');
        } else {
            bar.classList.add('bg-danger');
            bar.classList.remove('bg-success', 'bg-warning');
        }
    });
}

// Dashboard refresh functionality - automatically reload every 5 minutes
function setupDashboardAutoRefresh() {
    const refreshInterval = 5 * 60 * 1000; // 5 minutes in milliseconds
    setInterval(() => {
        location.reload();
    }, refreshInterval);
}

// Initialize when document is ready
document.addEventListener('DOMContentLoaded', function() {
    // Format all time elements that have the timeago class
    document.querySelectorAll('.timeago').forEach(function(elem) {
        const originalDateTime = elem.getAttribute('datetime');
        if (originalDateTime) {
            elem.textContent = formatTimeAgo(originalDateTime);
        }
    });

    // Setup auto-refresh
    setupDashboardAutoRefresh();
});

// Handle click on the refresh button
function refreshDashboard() {
    location.reload();
}
