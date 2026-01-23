namespace TestSolution.Core;

public class UserValidator : IValidator<User>
{
    public bool Validate(User entity, out List<string> errors)
    {
        errors = new List<string>();
        if (string.IsNullOrWhiteSpace(entity.Name))
            errors.Add("Name is required.");
        if (string.IsNullOrWhiteSpace(entity.Email))
            errors.Add("Email is required.");
        if (entity.Email != null && !entity.Email.Contains('@'))
            errors.Add("Email format is invalid.");
        return errors.Count == 0;
    }
}

public class OrderValidator : IValidator<Order>
{
    public bool Validate(Order entity, out List<string> errors)
    {
        errors = new List<string>();
        if (entity.UserId <= 0)
            errors.Add("UserId must be positive.");
        if (entity.Items == null || entity.Items.Count == 0)
            errors.Add("Order must have at least one item.");
        if (entity.Total < 0)
            errors.Add("Total cannot be negative.");
        foreach (var item in entity.Items ?? new())
        {
            if (item.Quantity <= 0)
                errors.Add($"Item {item.ProductName} must have positive quantity.");
            if (item.UnitPrice <= 0)
                errors.Add($"Item {item.ProductName} must have positive price.");
        }
        return errors.Count == 0;
    }
}

public class ProductValidator : IValidator<Product>
{
    public bool Validate(Product entity, out List<string> errors)
    {
        errors = new List<string>();
        if (string.IsNullOrWhiteSpace(entity.Name))
            errors.Add("Product name is required.");
        if (entity.Price <= 0)
            errors.Add("Price must be positive.");
        if (entity.Stock < 0)
            errors.Add("Stock cannot be negative.");
        if (string.IsNullOrWhiteSpace(entity.Category))
            errors.Add("Category is required.");
        return errors.Count == 0;
    }
}
