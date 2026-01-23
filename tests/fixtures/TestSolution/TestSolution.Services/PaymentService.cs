using TestSolution.Core;

namespace TestSolution.Services;

public class PaymentService
{
    private readonly IOrderRepository _orderRepo;
    private readonly IAuditLogger _auditLogger;

    public PaymentService(IOrderRepository orderRepo, IAuditLogger auditLogger)
    {
        _orderRepo = orderRepo;
        _auditLogger = auditLogger;
    }

    public async Task<Payment> ProcessPaymentAsync(int orderId, decimal amount, string method)
    {
        var order = await _orderRepo.GetByIdAsync(orderId);
        if (order == null) throw new InvalidOperationException("Order not found");
        if (!ValidatePaymentAmount(amount, order.UserId))
            throw new InvalidOperationException("Invalid payment amount");

        var payment = new Payment { OrderId = orderId, Amount = amount, Method = method, IsProcessed = true };
        await _auditLogger.LogAsync("ProcessPayment", order.UserId.ToString(), $"Payment of {amount} for order {orderId}");
        return payment;
    }

    // Intentional duplicate of OrderService.ValidateOrderAmount
    public bool ValidatePaymentAmount(decimal amount, int userId)
    {
        if (amount <= 0) return false;
        if (amount > 10000) return false;
        if (userId <= 0) return false;
        return true;
    }

    public async Task<bool> RefundPaymentAsync(int orderId, decimal amount)
    {
        var order = await _orderRepo.GetByIdAsync(orderId);
        if (order == null) return false;
        if (amount > order.Total) return false;

        await _auditLogger.LogAsync("RefundPayment", order.UserId.ToString(), $"Refund of {amount} for order {orderId}");
        return true;
    }

    public bool IsValidPaymentMethod(string method)
    {
        return method switch
        {
            "credit_card" => true,
            "debit_card" => true,
            "paypal" => true,
            "bank_transfer" => true,
            _ => false
        };
    }

    // Complex method
    public async Task<List<Payment>> ProcessBatchPaymentsAsync(List<(int OrderId, decimal Amount, string Method)> payments)
    {
        var results = new List<Payment>();
        foreach (var (orderId, amount, method) in payments)
        {
            try
            {
                if (IsValidPaymentMethod(method))
                {
                    if (ValidatePaymentAmount(amount, orderId))
                    {
                        var order = await _orderRepo.GetByIdAsync(orderId);
                        if (order != null)
                        {
                            if (order.Status == OrderStatus.Pending || order.Status == OrderStatus.Processing)
                            {
                                var payment = new Payment { OrderId = orderId, Amount = amount, Method = method, IsProcessed = true };
                                results.Add(payment);
                                order.Status = OrderStatus.Processing;
                                await _orderRepo.UpdateAsync(order);
                            }
                        }
                    }
                }
            }
            catch
            {
                // Log and continue
            }
        }
        return results;
    }
}
