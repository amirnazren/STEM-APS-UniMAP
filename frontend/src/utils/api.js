/**
 * @file REST helpers: pre-configured axios client, error extraction and file downloads.
 */
import axios from 'axios';

/** Backend base URL (REST + Socket.io). Override with REACT_APP_BACKEND_URL. */
export const BACKEND_URL = (process.env.REACT_APP_BACKEND_URL || 'http://localhost:3001').replace(/\/+$/, '');

/** Shared axios instance for all dashboard REST calls. */
export const api = axios.create({
  baseURL: `${BACKEND_URL}/api`,
  timeout: 15000,
  headers: { 'Content-Type': 'application/json' }
});

/**
 * Turn any axios/network error into a short, teacher-friendly message.
 * Handles JSON bodies, Blob bodies (from `responseType: 'blob'` requests) and network failures.
 *
 * @param {unknown} err
 * @param {string} [fallback]
 * @returns {Promise<string>}
 */
export async function getErrorMessage(err, fallback = 'Something went wrong. Please try again.') {
  if (!err) return fallback;
  if (axios.isCancel(err)) return 'Request cancelled.';
  const res = err.response;
  if (!res) {
    if (err.code === 'ECONNABORTED') return 'The server took too long to respond.';
    return `Cannot reach the server at ${BACKEND_URL}. Is the backend running?`;
  }
  let data = res.data;
  if (typeof Blob !== 'undefined' && data instanceof Blob) {
    try {
      data = JSON.parse(await data.text());
    } catch (e) {
      data = null;
    }
  }
  if (data && typeof data === 'object') return data.error || data.reason || data.message || fallback;
  return `${fallback} (HTTP ${res.status})`;
}

/**
 * Extract a filename from a Content-Disposition header.
 * @param {string|undefined} header
 * @param {string} fallback
 * @returns {string}
 */
export function filenameFromDisposition(header, fallback) {
  if (!header) return fallback;
  const utf8 = /filename\*=UTF-8''([^;]+)/i.exec(header);
  if (utf8) return decodeURIComponent(utf8[1]);
  const plain = /filename="?([^";]+)"?/i.exec(header);
  return plain ? plain[1] : fallback;
}

/**
 * Trigger a browser download for a Blob.
 * @param {Blob} blob
 * @param {string} filename
 */
export function downloadBlob(blob, filename) {
  const url = URL.createObjectURL(blob);
  const a = document.createElement('a');
  a.href = url;
  a.download = filename;
  document.body.appendChild(a);
  a.click();
  a.remove();
  // Give the browser a moment to start the download before revoking.
  setTimeout(() => URL.revokeObjectURL(url), 1000);
}

/**
 * GET a binary endpoint and download it.
 * @param {string} path API path relative to /api (e.g. `/reports/export-csv/1234`).
 * @param {string} fallbackName Filename used when the server sends none.
 * @returns {Promise<void>}
 */
export async function downloadFromApi(path, fallbackName) {
  const res = await api.get(path, { responseType: 'blob', timeout: 60000 });
  downloadBlob(res.data, filenameFromDisposition(res.headers['content-disposition'], fallbackName));
}
