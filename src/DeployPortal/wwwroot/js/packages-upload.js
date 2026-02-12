export function initDropZone(elementId, baseUri, dotNetRef) {
  const el = document.getElementById(elementId);
  if (!el) return;

  el.addEventListener('dragover', (e) => {
    e.preventDefault();
    e.stopPropagation();
    dotNetRef.invokeMethodAsync('SetDropZoneActive', true);
  });

  el.addEventListener('dragleave', (e) => {
    e.preventDefault();
    e.stopPropagation();
    dotNetRef.invokeMethodAsync('SetDropZoneActive', false);
  });

  el.addEventListener('drop', async (e) => {
    e.preventDefault();
    e.stopPropagation();
    dotNetRef.invokeMethodAsync('SetDropZoneActive', false);

    const dt = e.dataTransfer;
    if (!dt || !dt.files || dt.files.length === 0) return;

    const files = Array.from(dt.files).filter(f => f.name.toLowerCase().endsWith('.zip'));
    if (files.length === 0) return;

    const options = await dotNetRef.invokeMethodAsync('GetUploadOptions');
    let successCount = 0;
    let failCount = 0;

    for (const file of files) {
      const form = new FormData();
      form.append('file', file);
      if (options.packageType) form.append('packageType', options.packageType);
      if (options.devOpsTaskUrl) form.append('devOpsTaskUrl', options.devOpsTaskUrl);

      try {
        const r = await fetch(`${baseUri}/api/packages/upload`, {
          method: 'POST',
          body: form
        });
        if (r.ok) successCount++; else failCount++;
      } catch (_) {
        failCount++;
      }
    }

    await dotNetRef.invokeMethodAsync('OnDropZoneUploadComplete', successCount, failCount);
  });
}
