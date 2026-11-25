using ClientsDemo.Models;
using Microsoft.EntityFrameworkCore;

namespace ClientsDemo.Data;

public class ClientsDemoContext : DbContext
{
    public ClientsDemoContext(DbContextOptions<ClientsDemoContext> options) : base(options)
    {
    }

    public DbSet<Client> Clients => Set<Client>();
}
