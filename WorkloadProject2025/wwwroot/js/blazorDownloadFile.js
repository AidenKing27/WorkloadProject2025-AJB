// Simple helper to download a base64 string as a file.
// Place this file at wwwroot/js/blazorDownloadFile.js and reference it from index.html / _Host.cshtml.
window.blazorDownloadFile = (fileName, base64Data, contentType) => {
    try {
        const link = document.createElement('a');
        link.href = "data:" + (contentType || "application/octet-stream") + ";base64," + base64Data;
        link.setAttribute('download', fileName);
        document.body.appendChild(link);
        link.click();
        document.body.removeChild(link);
        return true;
    } catch (err) {
        console.error("blazorDownloadFile failed:", err);
        return false;
    }
};