/**
 * @file Express 404 and central error-handling middleware.
 */

'use strict';

/**
 * Catch-all for unknown routes.
 * @type {import('express').RequestHandler}
 */
function notFound(req, res) {
  res.status(404).json({ error: `Route not found: ${req.method} ${req.originalUrl}` });
}

/**
 * Central error handler. Maps well-known error types to HTTP status codes and
 * hides internal details in production.
 * @type {import('express').ErrorRequestHandler}
 */
// eslint-disable-next-line no-unused-vars
function errorHandler(err, req, res, next) {
  let status = err.status || err.statusCode || 500;
  let message = err.message || 'Internal server error';

  if (err.type === 'entity.parse.failed') {
    status = 400;
    message = 'Malformed JSON body';
  } else if (err.type === 'entity.too.large') {
    status = 413;
    message = 'Request body too large';
  } else if (err.name === 'ValidationError' || err.name === 'CastError') {
    status = 400;
  } else if (err.code === 11000) {
    status = 409;
    message = 'Duplicate key';
  }

  if (status >= 500) {
    console.error(`[error] ${req.method} ${req.originalUrl}:`, err);
    if (process.env.NODE_ENV === 'production') message = 'Internal server error';
  }

  if (res.headersSent) {
    // Streaming responses (PDF/CSV) may already have started; just terminate.
    res.end();
    return;
  }
  res.status(status).json({ error: message });
}

module.exports = { notFound, errorHandler };
