namespace TestSolution.Core;

public class UserRepository : IUserRepository
{
    private readonly List<User> _users = new();

    public Task<User?> GetByIdAsync(int id) => Task.FromResult(_users.FirstOrDefault(u => u.Id == id));
    public Task<List<User>> GetAllAsync() => Task.FromResult(_users.ToList());
    public Task<User> CreateAsync(User entity) { _users.Add(entity); return Task.FromResult(entity); }
    public Task UpdateAsync(User entity) { var i = _users.FindIndex(u => u.Id == entity.Id); if (i >= 0) _users[i] = entity; return Task.CompletedTask; }
    public Task DeleteAsync(int id) { _users.RemoveAll(u => u.Id == id); return Task.CompletedTask; }
    public Task<User?> GetByEmailAsync(string email) => Task.FromResult(_users.FirstOrDefault(u => u.Email == email));
    public Task<List<User>> GetActiveUsersAsync() => Task.FromResult(_users.Where(u => u.IsActive).ToList());
    public Task<List<User>> SearchUsersAsync(string query) => Task.FromResult(_users.Where(u => u.Name.Contains(query) || u.Email.Contains(query)).ToList());
}

public class OrderRepository : IOrderRepository
{
    private readonly List<Order> _orders = new();

    public Task<Order?> GetByIdAsync(int id) => Task.FromResult(_orders.FirstOrDefault(o => o.Id == id));
    public Task<List<Order>> GetAllAsync() => Task.FromResult(_orders.ToList());
    public Task<Order> CreateAsync(Order entity) { _orders.Add(entity); return Task.FromResult(entity); }
    public Task UpdateAsync(Order entity) { var i = _orders.FindIndex(o => o.Id == entity.Id); if (i >= 0) _orders[i] = entity; return Task.CompletedTask; }
    public Task DeleteAsync(int id) { _orders.RemoveAll(o => o.Id == id); return Task.CompletedTask; }
    public Task<List<Order>> GetByUserIdAsync(int userId) => Task.FromResult(_orders.Where(o => o.UserId == userId).ToList());
    public Task<List<Order>> GetByStatusAsync(OrderStatus status) => Task.FromResult(_orders.Where(o => o.Status == status).ToList());
    public Task<decimal> GetTotalRevenueAsync() => Task.FromResult(_orders.Sum(o => o.Total));
}

public class ProductRepository : IProductRepository
{
    private readonly List<Product> _products = new();

    public Task<Product?> GetByIdAsync(int id) => Task.FromResult(_products.FirstOrDefault(p => p.Id == id));
    public Task<List<Product>> GetAllAsync() => Task.FromResult(_products.ToList());
    public Task<Product> CreateAsync(Product entity) { _products.Add(entity); return Task.FromResult(entity); }
    public Task UpdateAsync(Product entity) { var i = _products.FindIndex(p => p.Id == entity.Id); if (i >= 0) _products[i] = entity; return Task.CompletedTask; }
    public Task DeleteAsync(int id) { _products.RemoveAll(p => p.Id == id); return Task.CompletedTask; }
    public Task<List<Product>> GetByCategoryAsync(string category) => Task.FromResult(_products.Where(p => p.Category == category).ToList());
    public Task<List<Product>> GetLowStockAsync(int threshold) => Task.FromResult(_products.Where(p => p.Stock < threshold).ToList());
}

public class NotificationRepository : INotificationRepository
{
    private readonly List<Notification> _notifications = new();

    public Task<Notification?> GetByIdAsync(int id) => Task.FromResult(_notifications.FirstOrDefault(n => n.Id == id));
    public Task<List<Notification>> GetAllAsync() => Task.FromResult(_notifications.ToList());
    public Task<Notification> CreateAsync(Notification entity) { _notifications.Add(entity); return Task.FromResult(entity); }
    public Task UpdateAsync(Notification entity) { var i = _notifications.FindIndex(n => n.Id == entity.Id); if (i >= 0) _notifications[i] = entity; return Task.CompletedTask; }
    public Task DeleteAsync(int id) { _notifications.RemoveAll(n => n.Id == id); return Task.CompletedTask; }
    public Task<List<Notification>> GetUnreadAsync(int userId) => Task.FromResult(_notifications.Where(n => n.UserId == userId && !n.IsRead).ToList());
    public Task MarkAsReadAsync(int notificationId) { var n = _notifications.FirstOrDefault(x => x.Id == notificationId); if (n != null) n.IsRead = true; return Task.CompletedTask; }
}
