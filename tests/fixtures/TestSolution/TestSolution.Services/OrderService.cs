using TestSolution.Core;

namespace TestSolution.Services;

public class OrderService
{
    private readonly IOrderRepository _orderRepo;
    private readonly IProductRepository _productRepo;
    private readonly IValidator<Order> _validator;
    private readonly IAuditLogger _auditLogger;

    public OrderService(IOrderRepository orderRepo, IProductRepository productRepo, IValidator<Order> validator, IAuditLogger auditLogger)
    {
        _orderRepo = orderRepo;
        _productRepo = productRepo;
        _validator = validator;
        _auditLogger = auditLogger;
    }

    public async Task<Order> CreateOrderAsync(int userId, List<OrderItem> items)
    {
        var order = new Order
        {
            UserId = userId,
            Items = items,
            Total = items.Sum(i => i.TotalPrice),
            Status = OrderStatus.Pending,
            OrderDate = DateTime.UtcNow
        };

        if (!_validator.Validate(order, out var errors))
            throw new InvalidOperationException(string.Join(", ", errors));

        var created = await _orderRepo.CreateAsync(order);
        await _auditLogger.LogAsync("CreateOrder", userId.ToString(), $"Order {created.Id} created with {items.Count} items");
        return created;
    }

    public async Task<Order?> GetOrderByIdAsync(int orderId) => await _orderRepo.GetByIdAsync(orderId);

    public async Task<List<Order>> GetUserOrdersAsync(int userId) => await _orderRepo.GetByUserIdAsync(userId);

    public async Task CancelOrderAsync(int orderId)
    {
        var order = await _orderRepo.GetByIdAsync(orderId);
        if (order == null) throw new InvalidOperationException("Order not found");
        if (order.Status == OrderStatus.Shipped || order.Status == OrderStatus.Delivered)
            throw new InvalidOperationException("Cannot cancel shipped/delivered order");
        order.Status = OrderStatus.Cancelled;
        await _orderRepo.UpdateAsync(order);
        await _auditLogger.LogAsync("CancelOrder", order.UserId.ToString(), $"Order {orderId} cancelled");
    }

    public async Task UpdateOrderStatusAsync(int orderId, OrderStatus newStatus)
    {
        var order = await _orderRepo.GetByIdAsync(orderId);
        if (order == null) throw new InvalidOperationException("Order not found");
        order.Status = newStatus;
        await _orderRepo.UpdateAsync(order);
    }

    // Intentional duplicate of validation logic (same pattern as PaymentService.ValidatePaymentAmount)
    public bool ValidateOrderAmount(decimal amount, int userId)
    {
        if (amount <= 0) return false;
        if (amount > 10000) return false;
        if (userId <= 0) return false;
        return true;
    }

    // Complex method for testing
    public async Task<List<Order>> ProcessOrderBatchAsync(List<Order> orders)
    {
        var processed = new List<Order>();
        foreach (var order in orders)
        {
            try
            {
                if (order.Status == OrderStatus.Pending)
                {
                    if (order.Items.Count > 0)
                    {
                        foreach (var item in order.Items)
                        {
                            var product = await _productRepo.GetByIdAsync(item.Id);
                            if (product != null)
                            {
                                if (product.Stock >= item.Quantity)
                                {
                                    product.Stock -= item.Quantity;
                                    await _productRepo.UpdateAsync(product);
                                }
                                else
                                {
                                    order.Status = OrderStatus.Cancelled;
                                    break;
                                }
                            }
                        }
                        if (order.Status != OrderStatus.Cancelled)
                        {
                            order.Status = OrderStatus.Processing;
                        }
                    }
                }
                await _orderRepo.UpdateAsync(order);
                processed.Add(order);
            }
            catch
            {
                order.Status = OrderStatus.Cancelled;
                await _orderRepo.UpdateAsync(order);
            }
        }
        return processed;
    }
}
