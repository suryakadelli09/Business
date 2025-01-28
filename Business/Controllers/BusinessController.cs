using Business.Data;
using Business.Models;
using Business.Dto;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Win32;
using Microsoft.EntityFrameworkCore;
using Banking_Application.Models;
using Microsoft.IdentityModel.Tokens;
using Registration.Models;
using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Text;
using System.Net.Http;
using Business.Service;

namespace Business.Controllers
{
    [Route("api/[controller]")]
    [ApiController]
    public class BusinessController : ControllerBase
    {
        private readonly BusinessContext _context;
        public ILogger<BusinessController> _logger;
        private readonly IConfiguration _configuration;
        private readonly string _apiKey;
        private readonly GeocodingService _geocodingService;

        private readonly string _uploadsFolder = Path.Combine(Directory.GetCurrentDirectory(), "uploads");
        public BusinessController(ILogger<BusinessController> logger, BusinessContext context, HttpClient httpClient, IConfiguration configuration, GeocodingService geocodingService)
        {
            _context = context;
            _logger = logger;
            _apiKey = configuration["GoogleMaps:ApiKey"]; // API key stored in configuration
            _geocodingService = geocodingService;
        }

        [HttpGet("geocode")]
        public async Task<IActionResult> GeocodeAsync(string address)
        {
            var location = await _geocodingService.GeocodeAsync(address);
            if (location == null)
            {
                return NotFound("Geocoding failed.");
            }

            return Ok(location);
        }

        [HttpGet("{imageName}")]
        public IActionResult GetImage(string imageName)
        {
            var filePath = Path.Combine(_uploadsFolder, imageName);
            if (!System.IO.File.Exists(filePath))
            {
                return NotFound();
            }

            var fileBytes = System.IO.File.ReadAllBytes(filePath);
            return File(fileBytes, "image/jpeg"); // Adjust MIME type as needed
        }

        [HttpPost]
        public async Task<ActionResult<bool>> BusinessRegistration([FromForm] BusinesDto businesDto)
        {
            if (businesDto.VisitingCard != null)
            {

                string currentDirectory = Directory.GetCurrentDirectory();
                string uploadsFolderPath = Path.Combine(currentDirectory, "wwwroot", "VisitingCards", businesDto.VisitingCard.FileName);
                Console.WriteLine("Uploads Folder Path: " + uploadsFolderPath);

                using (var stream = new FileStream(uploadsFolderPath, FileMode.Create))
                {
                    await businesDto.VisitingCard.CopyToAsync(stream);
                }

                bool isRegistered = await _context.Businesses.AnyAsync(u => u.EmailId == businesDto.EmailId && u.Name == businesDto.Name);
                if (isRegistered)
                {
                    //return Ok(new { message = "Email is already registered." });
                    return Conflict(new { message = "Email is already registered." });
                }
                string hashedPassword = BCrypt.Net.BCrypt.HashPassword(businesDto.Password);
                var business = new Busines
                {
                    Name = businesDto.Name,
                    EmailId = businesDto.EmailId,
                    Password = hashedPassword,
                    Description = businesDto.Description,
                    Location = businesDto.Location,
                    Latitude = businesDto.Latitude,
                    Longitude = businesDto.Longitude,
                    VisitingCard = uploadsFolderPath,
                    CategoryID = businesDto.CategoryID,
                    SubCategoryID = businesDto.SubCategoryID
                };
                _context.Businesses.Add(business);
                int regStatus = await _context.SaveChangesAsync();
                return Ok(true);
            }

            return BadRequest(false);
        }



        [HttpGet("GetCategories")]
        public async Task<IActionResult> GetCategories()
        {
            var categories = await _context.Categories
                .Select(c => new
                {
                    c.CategoryID,
                    c.CategoryName
                })
                .ToListAsync();

            return Ok(categories);
        }

        [HttpGet("GetSubCategories/{categoryId}")]
        public async Task<IActionResult> GetSubCategories(int categoryId)
        {
            var subCategories = await _context.SubCategories
                .Where(sc => sc.CategoryID == categoryId)
                .Select(sc => new
                {
                    sc.SubCategoryID,
                    sc.SubCategoryName
                })
                .ToListAsync();

            return Ok(subCategories);
        }

        [HttpGet("search")]
        public async Task<IActionResult> SearchBusinesses(string category, string subcategory)
        {
            try
            {
                
                var businesses = await _context.Businesses
                .Include(b => b.SubCategory)
                .ThenInclude(sc => sc.Category)
                .Where(b => b.SubCategory.Category.CategoryName == category && b.SubCategory.SubCategoryName == subcategory)
                .Select(b => new BusinessDataShow
                {
                    BusinessID = b.BusinessID,
                    Name = b.Name,
                    Description = b.Description,
                    Distancekm = b.Latitude + b.Longitude,
                    VisitingCard = b.VisitingCard,
                    longitude = b.Longitude,
                    Latitude = b.Latitude
                })
                .ToListAsync();
                return Ok(businesses);
            }
            catch (Exception)
            {
                throw;
            }
        }
        [HttpPut]
        public async Task<ActionResult<bool>> UpdateBusiness([FromForm] BusinesDto businesDto)
        {
            try
            {
                // Find the business by ID
                var existingBusiness = await _context.Businesses.FindAsync(businesDto.BusinessID);
                if (existingBusiness == null)
                {
                    return NotFound(new { message = "Business not found." });
                }

                // Check if the email or business name is being changed and if it's already registered
                bool isDuplicate = await _context.Businesses.AnyAsync(b => b.EmailId == businesDto.EmailId && b.Name == businesDto.Name && b.BusinessID != businesDto.BusinessID);
                if (isDuplicate)
                {
                    return BadRequest(new { message = "Email and/or Business Name already registered." });
                }

                // If a new visiting card is uploaded, update the file path
                if (businesDto.VisitingCard != null)
                {
                    // Delete the old visiting card file if it exists
                    if (System.IO.File.Exists(existingBusiness.VisitingCard))
                    {
                        System.IO.File.Delete(existingBusiness.VisitingCard);
                    }

                    string currentDirectory = Directory.GetCurrentDirectory();
                    string filePath = Path.Combine(currentDirectory, "uploads", businesDto.VisitingCard.FileName);

                    using (var stream = new FileStream(filePath, FileMode.Create))
                    {
                        await businesDto.VisitingCard.CopyToAsync(stream);
                    }
                    existingBusiness.VisitingCard = filePath;
                }

                // Update the business details
                existingBusiness.Name = businesDto.Name;
                existingBusiness.EmailId = businesDto.EmailId;
                existingBusiness.Description = businesDto.Description;
                existingBusiness.Location = businesDto.Location;
                existingBusiness.Latitude = businesDto.Latitude;
                existingBusiness.Longitude = businesDto.Longitude;
                existingBusiness.CategoryID = businesDto.CategoryID;
                existingBusiness.SubCategoryID = businesDto.SubCategoryID;

                // If password is provided, hash and update it
                if (!string.IsNullOrEmpty(businesDto.Password))
                {
                    string hashedPassword = BCrypt.Net.BCrypt.HashPassword(businesDto.Password);
                    existingBusiness.Password = hashedPassword;
                }

                // Save the changes to the database
                _context.Businesses.Update(existingBusiness);
                int updateStatus = await _context.SaveChangesAsync();

                return Ok(true);
            }
            catch (Exception ex)
            {
                return StatusCode(500, $"Internal server error: {ex.Message}");
            }
        }

        [HttpGet("getbusinessdetailbyid/{id}")]
        public async Task<IActionResult> GetBusineesDetailById(int id)
        {
            try
            {
                var businesses = await _context.Businesses.Where(b => b.BusinessID == id).Select(b => new Busines
                {
                    BusinessID = b.BusinessID,
                    Name = b.Name,
                    EmailId = b.EmailId,
                    Password = b.Password,
                    Description = b.Description,
                    Location = b.Location,
                    VisitingCard = b.VisitingCard,
                    Latitude = b.Latitude,
                    Longitude = b.Longitude,
                    CategoryID = b.CategoryID,
                    SubCategoryID = b.SubCategoryID
                }).ToListAsync();

                return Ok(businesses);
            }
            catch (Exception ex)
            {
                return StatusCode(500, $"Internal server error: {ex.Message}");
            }
        }
    }    
}
