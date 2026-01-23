using TestSolution.Services;
using TestSolution.Core;

namespace TestSolution.Api;

public class UserController
{
    private readonly UserService _userService;
    public UserController(UserService userService) { _userService = userService; }

    public async Task<User> CreateUser(string name, string email) => await _userService.CreateUserAsync(name, email);
    public async Task<User?> GetUser(int id) => await _userService.GetUserByIdAsync(id);
    public async Task<List<User>> GetActiveUsers() => await _userService.GetActiveUsersAsync();
    public async Task DeactivateUser(int id) => await _userService.DeactivateUserAsync(id);
    public async Task AddTag(int userId, string tag) => await _userService.AddTagToUserAsync(userId, tag);
    public async Task RemoveTag(int userId, string tag) => await _userService.RemoveTagFromUserAsync(userId, tag);
}

public class OrderController
{
    private readonly OrderService _orderService;
    public OrderController(OrderService orderService) { _orderService = orderService; }

    public async Task<Order> CreateOrder(int userId, List<OrderItem> items) => await _orderService.CreateOrderAsync(userId, items);
    public async Task<Order?> GetOrder(int id) => await _orderService.GetOrderByIdAsync(id);
    public async Task<List<Order>> GetUserOrders(int userId) => await _orderService.GetUserOrdersAsync(userId);
    public async Task CancelOrder(int id) => await _orderService.CancelOrderAsync(id);
    public async Task UpdateStatus(int id, OrderStatus status) => await _orderService.UpdateOrderStatusAsync(id, status);
}

public class PaymentController
{
    private readonly PaymentService _paymentService;
    public PaymentController(PaymentService paymentService) { _paymentService = paymentService; }

    public async Task<Payment> ProcessPayment(int orderId, decimal amount, string method)
        => await _paymentService.ProcessPaymentAsync(orderId, amount, method);
    public async Task<bool> Refund(int orderId, decimal amount)
        => await _paymentService.RefundPaymentAsync(orderId, amount);
    public bool ValidateMethod(string method) => _paymentService.IsValidPaymentMethod(method);
}

public class ProductController
{
    private readonly ProductService _productService;
    public ProductController(ProductService productService) { _productService = productService; }

    public async Task<Product?> GetProduct(int id) => await _productService.GetProductByIdAsync(id);
    public async Task<List<Product>> GetAllProducts() => await _productService.GetAllProductsAsync();
    public async Task<List<Product>> GetByCategory(string category) => await _productService.GetProductsByCategoryAsync(category);
    public async Task<Product> CreateProduct(string name, decimal price, int stock, string category)
        => await _productService.CreateProductAsync(name, price, stock, category);
    public async Task UpdateStock(int id, int change) => await _productService.UpdateStockAsync(id, change);
    public async Task<List<Product>> GetLowStock(int threshold = 10) => await _productService.GetLowStockProductsAsync(threshold);
}

public class NotificationController
{
    private readonly NotificationService _notificationService;
    public NotificationController(NotificationService notificationService) { _notificationService = notificationService; }

    public async Task Send(int userId, string message, NotificationType type)
        => await _notificationService.SendNotificationAsync(userId, message, type);
    public async Task<List<Notification>> GetUnread(int userId)
        => await _notificationService.GetUnreadNotificationsAsync(userId);
    public async Task MarkRead(int id) => await _notificationService.MarkNotificationAsReadAsync(id);
    public async Task SendBulk(List<int> userIds, string message, NotificationType type)
        => await _notificationService.SendBulkNotificationAsync(userIds, message, type);
}
