using TestSolution.Core;

namespace TestSolution.Services;

public class UserService
{
    private readonly IUserRepository _userRepo;
    private readonly IValidator<User> _validator;
    private readonly IAuditLogger _auditLogger;

    public UserService(IUserRepository userRepo, IValidator<User> validator, IAuditLogger auditLogger)
    {
        _userRepo = userRepo;
        _validator = validator;
        _auditLogger = auditLogger;
    }

    public async Task<User> CreateUserAsync(string name, string email)
    {
        var user = new User { Name = name, Email = email, IsActive = true, CreatedAt = DateTime.UtcNow };
        if (!_validator.Validate(user, out var errors))
            throw new InvalidOperationException(string.Join(", ", errors));
        var created = await _userRepo.CreateAsync(user);
        await _auditLogger.LogAsync("CreateUser", created.Id.ToString(), $"Created user {name}");
        return created;
    }

    public async Task<User?> GetUserByIdAsync(int id) => await _userRepo.GetByIdAsync(id);

    public async Task<User?> FindUserByEmailAsync(string email) => await _userRepo.GetByEmailAsync(email);

    public async Task<List<User>> GetActiveUsersAsync() => await _userRepo.GetActiveUsersAsync();

    public async Task DeactivateUserAsync(int userId)
    {
        var user = await _userRepo.GetByIdAsync(userId);
        if (user == null) throw new InvalidOperationException("User not found");
        user.IsActive = false;
        await _userRepo.UpdateAsync(user);
        await _auditLogger.LogAsync("DeactivateUser", userId.ToString(), $"Deactivated user {user.Name}");
    }

    public async Task AddTagToUserAsync(int userId, string tag)
    {
        var user = await _userRepo.GetByIdAsync(userId);
        if (user == null) throw new InvalidOperationException("User not found");
        if (!user.Tags.Contains(tag))
        {
            user.Tags.Add(tag);
            await _userRepo.UpdateAsync(user);
        }
    }

    public async Task RemoveTagFromUserAsync(int userId, string tag)
    {
        var user = await _userRepo.GetByIdAsync(userId);
        if (user == null) throw new InvalidOperationException("User not found");
        user.Tags.Remove(tag);
        await _userRepo.UpdateAsync(user);
    }

    // Intentionally complex method for testing cognitive complexity
    public async Task<List<User>> ProcessUserBatchAsync(List<User> users, bool validateAll, bool deactivateInvalid)
    {
        var results = new List<User>();
        foreach (var user in users)
        {
            if (validateAll)
            {
                if (_validator.Validate(user, out var errors))
                {
                    if (user.IsActive)
                    {
                        if (user.Tags.Count > 0)
                        {
                            foreach (var tag in user.Tags)
                            {
                                if (tag.StartsWith("vip"))
                                {
                                    user.Name = $"[VIP] {user.Name}";
                                    break;
                                }
                            }
                        }
                        results.Add(await _userRepo.CreateAsync(user));
                    }
                    else if (deactivateInvalid)
                    {
                        user.IsActive = false;
                        await _userRepo.UpdateAsync(user);
                    }
                }
                else
                {
                    if (deactivateInvalid)
                    {
                        user.IsActive = false;
                        await _userRepo.UpdateAsync(user);
                    }
                }
            }
            else
            {
                results.Add(await _userRepo.CreateAsync(user));
            }
        }
        return results;
    }
}
