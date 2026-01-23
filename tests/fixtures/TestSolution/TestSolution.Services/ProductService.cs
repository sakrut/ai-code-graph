using TestSolution.Core;

namespace TestSolution.Services;

public class ProductService
{
    private readonly IProductRepository _productRepo;
    private readonly IAuditLogger _auditLogger;

    public ProductService(IProductRepository productRepo, IAuditLogger auditLogger)
    {
        _productRepo = productRepo;
        _auditLogger = auditLogger;
    }

    public async Task<Product?> GetProductByIdAsync(int id) => await _productRepo.GetByIdAsync(id);
    public async Task<List<Product>> GetAllProductsAsync() => await _productRepo.GetAllAsync();
    public async Task<List<Product>> GetProductsByCategoryAsync(string category) => await _productRepo.GetByCategoryAsync(category);

    public async Task<Product> CreateProductAsync(string name, decimal price, int stock, string category)
    {
        var product = new Product { Name = name, Price = price, Stock = stock, Category = category };
        var created = await _productRepo.CreateAsync(product);
        await _auditLogger.LogAsync("CreateProduct", "system", $"Created product {name}");
        return created;
    }

    public async Task UpdateStockAsync(int productId, int quantityChange)
    {
        var product = await _productRepo.GetByIdAsync(productId);
        if (product == null) throw new InvalidOperationException("Product not found");
        product.Stock += quantityChange;
        if (product.Stock < 0) product.Stock = 0;
        await _productRepo.UpdateAsync(product);
    }

    public async Task<List<Product>> GetLowStockProductsAsync(int threshold = 10)
    {
        return await _productRepo.GetLowStockAsync(threshold);
    }

    public async Task ApplyDiscountAsync(int productId, decimal discountPercent)
    {
        var product = await _productRepo.GetByIdAsync(productId);
        if (product == null) throw new InvalidOperationException("Product not found");
        if (discountPercent < 0 || discountPercent > 100) throw new ArgumentException("Invalid discount");
        product.Price *= (1 - discountPercent / 100);
        await _productRepo.UpdateAsync(product);
    }
}
