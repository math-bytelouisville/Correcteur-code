namespace ClientsDemo.Services;

/// <summary>Journal en mémoire (projet fictif).</summary>
public class Journal
{
    private readonly List<string> _lignes = new();

    public void Ecrire(string? texte) => _lignes.Add(texte ?? "");
}
