using Microsoft.EntityFrameworkCore;
using RescueSriLanka.Api.Data;

namespace RescueSriLanka.Api.Tests
{
    public static class TestDbFactory
    {
        // Each test gets its own isolated in-memory database by using a
        // unique Guid as the database name — no real Postgres needed.
        public static ApplicationDbContext Create()
        {
            var options = new DbContextOptionsBuilder<ApplicationDbContext>()
                .UseInMemoryDatabase(Guid.NewGuid().ToString())
                .Options;

            return new ApplicationDbContext(options);
        }
    }
}
