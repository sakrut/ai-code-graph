namespace TestSolution.Core;

public interface IRepository<T> where T : class
{
    Task<T?> GetByIdAsync(int id);
    Task<List<T>> GetAllAsync();
    Task<T> CreateAsync(T entity);
    Task UpdateAsync(T entity);
    Task DeleteAsync(int id);
}

public interface IUserRepository : IRepository<User>
{
    Task<User?> GetByEmailAsync(string email);
    Task<List<User>> GetActiveUsersAsync();
    Task<List<User>> SearchUsersAsync(string query);
}

public interface IOrderRepository : IRepository<Order>
{
    Task<List<Order>> GetByUserIdAsync(int userId);
    Task<List<Order>> GetByStatusAsync(OrderStatus status);
    Task<decimal> GetTotalRevenueAsync();
}

public interface IProductRepository : IRepository<Product>
{
    Task<List<Product>> GetByCategoryAsync(string category);
    Task<List<Product>> GetLowStockAsync(int threshold);
}

public interface INotificationRepository : IRepository<Notification>
{
    Task<List<Notification>> GetUnreadAsync(int userId);
    Task MarkAsReadAsync(int notificationId);
}

public interface IValidator<T>
{
    bool Validate(T entity, out List<string> errors);
}

public interface IAuditLogger
{
    Task LogAsync(string action, string userId, string details);
}
