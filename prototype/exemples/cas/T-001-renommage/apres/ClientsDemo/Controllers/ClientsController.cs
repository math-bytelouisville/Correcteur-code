using ClientsDemo.Data;
using ClientsDemo.Models;
using ClientsDemo.Services;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace ClientsDemo.Controllers;

/// <summary>Gestion des clients (projet fictif de démonstration).</summary>
public class ClientsController : Controller
{
    private readonly ClientsDemoContext _contexte;
    private readonly ClientService _service;

    public ClientsController(ClientsDemoContext contexte, ClientService service)
    {
        _contexte = contexte;
        _service = service;
    }

    public async Task<IActionResult> Index()
    {
        var clients = await _contexte.Clients.OrderBy(c => c.Id).ToListAsync();
        return View(clients);
    }

    public async Task<IActionResult> Edit(int id)
    {
        var client = await _contexte.Clients.FindAsync(id);
        if (client is null)
        {
            return NotFound();
        }
        return View(client);
    }

    [HttpPost]
    public async Task<IActionResult> Edit(int id, string saisie)
    {
        var client = await _contexte.Clients.FindAsync(id);
        if (client is null) return NotFound();
        client.NomComplet = saisie.Trim();
        await _service.Enregistrer(client);
        return RedirectToAction(nameof(Index));
    }
}
