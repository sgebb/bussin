namespace Bussin.Models;

public class BlogArticle
{
    public string Slug { get; set; } = "";
    public string Title { get; set; } = "";
    public string Description { get; set; } = "";
    public string Tag { get; set; } = "";
    public string Date { get; set; } = "";
    public bool Featured { get; set; }
    public string Content { get; set; } = "";
}
