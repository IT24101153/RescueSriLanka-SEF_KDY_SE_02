using Microsoft.EntityFrameworkCore;
using RescueSriLanka.Api.Data;

namespace RescueSriLanka.Api.Tests
{
    public static class TestDbFactory
    {
        // Each test gets its own isolated in-memory database by using a
        // unique Guid as the database name — no real Postgres needed.
        public static ComponentDDbContext Create()
        {
            var options = new DbContextOptionsBuilder<ComponentDDbContext>()
                .UseInMemoryDatabase(Guid.NewGuid().ToString())
                .Options;

            return new ComponentDDbContext(options);
        }
    }
}
