const releaseApi = "https://api.github.com/repos/xXKuroiKenshiXx/SANABI-GESTOR/releases/latest";
const latestRelease = "https://github.com/xXKuroiKenshiXx/SANABI-GESTOR/releases/latest";
const assetNames = {
  windows: "SanabiGestor-windows-x64.exe",
  linux: "SanabiGestor-x86_64.AppImage",
};

function setReleaseFallback() {
  document.querySelectorAll("[data-download]").forEach((link) => {
    link.href = `${latestRelease}/download/${assetNames[link.dataset.download]}`;
  });
  document.querySelector("#release-summary").textContent = "Descargas de la última release pública";
  document.querySelector("#release-version").textContent = "LATEST";
  document.querySelector("#release-date").textContent = "Abrí GitHub para ver la versión y sus notas.";
}

async function loadLatestRelease() {
  try {
    const response = await fetch(releaseApi, {
      headers: { Accept: "application/vnd.github+json" },
      cache: "no-store",
    });
    if (!response.ok) throw new Error(`GitHub API ${response.status}`);
    const release = await response.json();
    if (release.draft || release.prerelease) throw new Error("No hay una release estable pública.");

    const assets = new Map((release.assets ?? []).map((asset) => [asset.name, asset.browser_download_url]));
    for (const [platform, filename] of Object.entries(assetNames)) {
      const link = document.querySelector(`[data-download="${platform}"]`);
      link.href = assets.get(filename) ?? `${latestRelease}/download/${filename}`;
    }

    const tag = release.tag_name || "Última versión";
    document.querySelector("#release-summary").textContent = `${tag} · portable y gratis`;
    document.querySelector("#release-version").textContent = tag;
    const published = release.published_at ? new Intl.DateTimeFormat("es", { dateStyle: "medium" }).format(new Date(release.published_at)) : "Versión estable";
    document.querySelector("#release-date").textContent = `Publicada el ${published}`;
    document.querySelector("#release-notes").href = release.html_url || latestRelease;
  } catch {
    setReleaseFallback();
  }
}

loadLatestRelease();
