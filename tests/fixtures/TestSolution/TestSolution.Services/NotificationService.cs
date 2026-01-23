using TestSolution.Core;

namespace TestSolution.Services;

public class NotificationService
{
    private readonly INotificationRepository _notificationRepo;
    private readonly IUserRepository _userRepo;

    public NotificationService(INotificationRepository notificationRepo, IUserRepository userRepo)
    {
        _notificationRepo = notificationRepo;
        _userRepo = userRepo;
    }

    public async Task SendNotificationAsync(int userId, string message, NotificationType type)
    {
        var user = await _userRepo.GetByIdAsync(userId);
        if (user == null || !user.IsActive) return;

        var notification = new Notification
        {
            UserId = userId,
            Message = message,
            Type = type,
            IsRead = false
        };
        await _notificationRepo.CreateAsync(notification);
    }

    public async Task<List<Notification>> GetUnreadNotificationsAsync(int userId)
    {
        return await _notificationRepo.GetUnreadAsync(userId);
    }

    public async Task MarkNotificationAsReadAsync(int notificationId)
    {
        await _notificationRepo.MarkAsReadAsync(notificationId);
    }

    public async Task SendBulkNotificationAsync(List<int> userIds, string message, NotificationType type)
    {
        foreach (var userId in userIds)
        {
            await SendNotificationAsync(userId, message, type);
        }
    }

    // Intentional duplicate pattern: tag management (same as UserService.AddTagToUserAsync)
    public async Task AddTagToNotificationAsync(int userId, string tag)
    {
        var user = await _userRepo.GetByIdAsync(userId);
        if (user == null) throw new InvalidOperationException("User not found");
        if (!user.Tags.Contains(tag))
        {
            user.Tags.Add(tag);
            await _userRepo.UpdateAsync(user);
        }
    }

    // Intentional duplicate pattern: tag removal (same as UserService.RemoveTagFromUserAsync)
    public async Task RemoveTagFromNotificationAsync(int userId, string tag)
    {
        var user = await _userRepo.GetByIdAsync(userId);
        if (user == null) throw new InvalidOperationException("User not found");
        user.Tags.Remove(tag);
        await _userRepo.UpdateAsync(user);
    }
}
