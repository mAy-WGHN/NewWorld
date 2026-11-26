/** "12,3 KB" for a file size. */
export function formatFileSize(bytes: number): string {
  if (bytes < 1024) {
    return `${bytes} B`;
  }
  const kb = bytes / 1024;
  return kb < 1024 ? `${kb.toFixed(1).replace('.', ',')} KB` : `${(kb / 1024).toFixed(1).replace('.', ',')} MB`;
}
