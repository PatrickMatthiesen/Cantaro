using Microsoft.AspNetCore.Identity;

namespace Cantaro.Api.Models;

public class User : IdentityUser<int>
{
    public DateTime CreatedAt { get; set; }
    public DateTime UpdatedAt { get; set; }
}
