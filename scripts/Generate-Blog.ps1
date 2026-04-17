# 1. Paths
$jsonPath = "src/wwwroot/blog/data/articles.json"
$outputDir = "src/wwwroot/blog"

# 2. Load Content
$articles = Get-Content $jsonPath | ConvertFrom-Json

# 3. Common Header & Footer snippets
$headerText = @"
<!DOCTYPE html>
<html lang="en">
<head>
    <meta charset="UTF-8">
    <meta name="viewport" content="width=device-width, initial-scale=1.0">
    <title>{{TITLE}} | Bussin Blog</title>
    <meta name="description" content="{{DESCRIPTION}}">
    <style>
        *, *::before, *::after { box-sizing: border-box; margin: 0; padding: 0; }
        body { font-family: 'Inter', system-ui, sans-serif; background: #f8fafc; color: #0f172a; line-height: 1.6; padding-top: 52px; }
        a { color: #0d6efd; }
        a:hover { text-decoration: underline; }
        .site-header { position: fixed; top: 0; left: 0; right: 0; z-index: 100; height: 52px; background: #fff; border-bottom: 1px solid #e2e8f0; display: flex; align-items: center; padding: 0 1.5rem; gap: 1.25rem; }
        .brand-root { color: #0d6efd; text-decoration: none !important; font-weight: 800; }
        .brand-root strong { color: #0d6efd; }
        .header-sep { color: #d1d5db; margin: 0 0.2rem; font-weight: 300; }
        .header-section { color: #6b7280; font-weight: 500; text-decoration: none !important; }
        .header-section:hover { color: #0f172a; }
        .header-back { font-size: 0.8rem; color: #6b7280; text-decoration: none !important; font-weight: 500; flex-shrink: 0; }
        .header-back:hover { color: #0f172a; }
        .header-spacer { flex: 1; }
        .header-cta { flex-shrink: 0; font-size: 0.8rem; font-weight: 700; color: #fff !important; background: #0f172a; padding: 0.45rem 1rem; border-radius: 6px; text-decoration: none !important; letter-spacing: 0.01em; transition: background 0.15s; }
        .header-cta:hover { background: #0d6efd !important; }
        article { max-width: 800px; margin: 0 auto; padding: 2.5rem 2rem 5rem; }
        .tag { display: inline-block; font-size: 0.65rem; font-weight: 700; text-transform: uppercase; letter-spacing: 0.08em; color: #0d6efd; background: #eff6ff; padding: 0.18rem 0.5rem; border-radius: 99px; margin-bottom: 1.1rem; }
        h1 { font-size: 2.25rem; font-weight: 900; color: #0f172a; line-height: 1.15; letter-spacing: -0.03em; margin-bottom: 1rem; }
        .lead { font-size: 1.1rem; color: #475569; line-height: 1.7; margin-bottom: 2.5rem; padding-bottom: 2rem; border-bottom: 1px solid #e2e8f0; }
        h2 { font-size: 1.35rem; font-weight: 800; color: #0f172a; margin: 2.25rem 0 0.65rem; }
        h3 { font-size: 1.1rem; font-weight: 700; color: #0f172a; margin: 1.5rem 0 0.5rem; }
        p { font-size: 1rem; color: #334155; line-height: 1.75; margin-bottom: 1.1rem; }
        ul, ol { color: #334155; font-size: 1rem; line-height: 1.75; padding-left: 1.5rem; margin-bottom: 1.25rem; }
        li { margin-bottom: 0.35rem; }
        strong { color: #0f172a; }
        code { background: #f1f5f9; color: #0f172a; padding: 0.1rem 0.35rem; border-radius: 4px; font-size: 0.875em; font-family: monospace; }
        .callout { background: #f0f7ff; border-left: 3px solid #0d6efd; border-radius: 6px; padding: 1rem 1.25rem; margin: 1.75rem 0; color: #1e3a5f; font-size: 0.95rem; line-height: 1.65; }
        .tool-box { background: #fff; border: 1.5px solid #e2e8f0; border-radius: 12px; padding: 1.5rem; margin-bottom: 1.25rem; }
        .tool-box.recommended { border-color: #bfdbfe; background: #f0f7ff; }
        .site-footer { background: #fff; border-top: 1px solid #e2e8f0; padding: 1rem 2rem; display: flex; justify-content: space-between; align-items: center; }
        .site-footer a.back { font-size: 0.85rem; color: #64748b; text-decoration: none; font-weight: 500; }
        .site-footer a.back:hover { color: #0f172a; }
        .site-footer a.cta { font-size: 0.8rem; font-weight: 700; color: #fff !important; background: #0f172a; padding: 0.4rem 0.9rem; border-radius: 6px; text-decoration: none !important; transition: background 0.15s; }
        .site-footer a.cta:hover { background: #0d6efd !important; }
    </style>
</head>
<body>
    <header class="site-header">
        <div class="header-brand">
            <a href="/" class="brand-root"><strong>bussin</strong></a><span class="header-sep">/</span><a href="/blog/index.html" class="header-section">blog</a>
        </div>
        <a href="/blog/index.html" class="header-back">← All articles</a>
        <span class="header-spacer"></span>
        <a href="/" class="header-cta">Open Bussin ↗</a>
    </header>
    <article>
"@

$footerText = @"
    </article>
    <footer class="site-footer">
        <a href="/blog/index.html" class="back">← All articles</a>
        <a href="/" class="cta">Open Bussin →</a>
    </footer>
</body>
</html>
"@

# 4. Generate Article Pages
foreach ($article in $articles) {
    Write-Host "Generating: $($article.slug).html"
    
    $fullHtml = $headerText -replace '{{TITLE}}', $article.title
    $fullHtml = $fullHtml -replace '{{DESCRIPTION}}', $article.description
    
    $fullHtml += "`n        <span class=`"tag`">$($article.tag)</span>"
    $fullHtml += "`n        <h1>$($article.title)</h1>"
    $fullHtml += "`n        <p class=`"lead`">$($article.description)</p>"
    $fullHtml += "`n        $($article.content)"
    $fullHtml += "`n$footerText"
    
    $fullHtml | Out-File -FilePath "$outputDir/$($article.slug).html" -Encoding utf8
}

# 5. Generate Index Page
Write-Host "Generating: index.html"
$indexHeader = $headerText -replace '{{TITLE}}', 'Bussin Blog — Azure Service Bus Guides, Tips & Best Practices'
$indexHeader = $indexHeader -replace '{{DESCRIPTION}}', 'Articles on Azure Service Bus: how to manage queues and topics, compare tools like Service Bus Explorer, best practices, security, and more.'
$indexHeader = $indexHeader -replace 'article {', 'main { width: 100%; max-width: 1000px; margin: 0 auto; padding: 2.5rem 2rem 4rem; } 
        .page-title { margin-bottom: 2rem; }
        .page-title h1 { font-size: 1.75rem; font-weight: 900; letter-spacing: -0.03em; margin-bottom: 0.2rem; }
        .page-title p { font-size: 0.9rem; color: #64748b; }
        .grid { display: grid; grid-template-columns: repeat(auto-fill, minmax(290px, 1fr)); gap: 1.25rem; }
        .card { display: flex; flex-direction: column; background: #fff; border: 1.5px solid #e2e8f0; border-radius: 14px; padding: 1.5rem; text-decoration: none !important; color: inherit; transition: border-color 0.2s, box-shadow 0.2s, transform 0.2s; }
        .card:hover { border-color: #0d6efd; box-shadow: 0 6px 24px rgba(13,110,253,0.1); transform: translateY(-2px); }
        .card.featured { border-color: #bfdbfe; background: #f0f7ff; }
        .card .tag { margin-bottom: 0.75rem; }
        .card h2 { font-size: 1.05rem; font-weight: 700; color: #0f172a; margin-bottom: 0.5rem; line-height: 1.35; }
        .card p { font-size: 0.875rem; color: #475569; line-height: 1.6; flex-grow: 1; margin-bottom: 1rem; }
        .card-meta { font-size: 0.75rem; color: #94a3b8; font-weight: 500; }
        footer { border-top: 1px solid #e2e8f0; padding: 2rem; text-align: center; color: #94a3b8; font-size: 0.8rem; }'

# Custom index body
$indexBody = @"
    <header class="site-header">
        <div class="header-brand">
            <a href="/" class="brand-root"><strong>bussin</strong></a><span class="header-sep">/</span><a href="/blog/index.html" class="header-section">blog</a>
        </div>
        <span class="header-spacer"></span>
        <a href="/" class="header-cta">Open Bussin ↗</a>
    </header>

    <main>
        <div class="page-title">
            <h1>Bussin Blog</h1>
            <p>Guides and deep dives on Azure Service Bus</p>
        </div>
        <div class="grid">
"@

foreach ($article in $articles) {
    $featuredClass = if ($article.featured) { "featured" } else { "" }
    $indexBody += @"
            <a href="/blog/$($article.slug).html" class="card $featuredClass">
                <span class="tag">$($article.tag)</span>
                <h2>$($article.title)</h2>
                <p>$($article.description)</p>
                <span class="card-meta">$($article.date)</span>
            </a>
"@
}

$indexBody += @"
        </div>
    </main>
    <footer>
        <p>Bussin is free and open source — <a href="https://github.com/sgebb/bussin" target="_blank">view on GitHub</a></p>
    </footer>
</body>
</html>
"@

# Clean up article specific css from index
$indexHtml = $indexHeader.Split('<header class="site-header">')[0] + $indexBody
$indexHtml | Out-File -FilePath "$outputDir/index.html" -Encoding utf8

Write-Host "Done!"
