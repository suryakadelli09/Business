using Banking_Application.Models;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.IdentityModel.Tokens;
using Registration.Models;
using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Text;
using Business.Data;
using Business.Models;
using System.Linq;

namespace Banking_Application.Controllers
{
    [Route("api/[controller]")]
    [ApiController]
    public class AuthController : ControllerBase
    {
        private readonly BusinessContext _context;
        private readonly IConfiguration _configuration;

        public AuthController(BusinessContext context, IConfiguration configuration)
        {
            _context = context;
            _configuration = configuration;
        }

        [HttpPost("register")]
        public async Task<IActionResult> Register(User user)
        {
            _context.Users.Add(user);
            await _context.SaveChangesAsync();
            return Ok(new { message = "User registered successfully!" });
        }

        [HttpPost("login")]
        public IActionResult Login(LoginRequest request)
        {
            var token = "";
            if (string.IsNullOrWhiteSpace(request.Username) || string.IsNullOrWhiteSpace(request.Password))
                return BadRequest("Username or password cannot be empty.");

            // Try to authenticate as Business
            var userBusiness = _context.Businesses
                .FirstOrDefault(u => u.EmailId == request.Username && u.Password == request.Password);

            // If not Business, try to authenticate as Customer
            if (userBusiness == null)
            {
                var userCustomer = _context.Customers
                    .FirstOrDefault(u => u.Cus_EmailId == request.Username && u.Cus_Password == request.Password);

                if (userCustomer == null)
                    return Unauthorized("Invalid username or password.");

                // Generate token for Customer
                token = GenerateCustomerToken(userCustomer);
                return Ok(new { token });
            }

            // Generate token for Business
            token = GenerateBusinessToken(userBusiness);
            return Ok(new { token });
        }

        private string GenerateBusinessToken(Busines business)
        {
            var claims = new[] {
                new Claim(ClaimTypes.Email, business.EmailId),
                new Claim("BusinessID", business.BusinessID.ToString())
            };

            var key = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(_configuration["Jwt:Key"]!));
            var creds = new SigningCredentials(key, SecurityAlgorithms.HmacSha256);

            var token = new JwtSecurityToken(
                issuer: _configuration["Jwt:Issuer"],
                audience: _configuration["Jwt:Audience"],
                claims: claims,
                expires: DateTime.Now.AddMinutes(5),
                signingCredentials: creds);

            return new JwtSecurityTokenHandler().WriteToken(token);
        }

        private string GenerateCustomerToken(Customer customer)
        {
            var claims = new[] {
                new Claim(ClaimTypes.Email, customer.Cus_EmailId),
                new Claim("Cus_Id", customer.Cus_Id.ToString())
            };

            var key = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(_configuration["Jwt:Key"]!));
            var creds = new SigningCredentials(key, SecurityAlgorithms.HmacSha256);

            var token = new JwtSecurityToken(
                issuer: _configuration["Jwt:Issuer"],
                audience: _configuration["Jwt:Audience"],
                claims: claims,
                expires: DateTime.Now.AddMinutes(5),
                signingCredentials: creds);

            return new JwtSecurityTokenHandler().WriteToken(token);
        }
    }
}
