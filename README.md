# Mutakamela AI Bot

A Flask-based insurance assistant for Mutakamela. The chatbot uses the product catalog and Adesso AI service to answer customer questions, recommend insurance products, and guide quote requests.

The chatbot project lives in [`backend/MutakamelaAI`](backend/MutakamelaAI/). Its detailed project documentation is in [`MutakamelaAI/README.md`](backend/MutakamelaAI/README.md).

## Quick Start

```sh
cd backend/MutakamelaAI
python3 -m venv venv
source venv/bin/activate
pip install -r requirements.txt
cp .env.example .env
```

Set `ADESSO_API_KEY` in `.env`, then start the web chat:

```sh
python web_chat.py
```

By default, the app listens on `http://127.0.0.1:5000`. Set `PORT` to use a different port.

## Configuration

- `ADESSO_API_KEY`: required API credential for the Adesso AI service.
- `ADESSO_API_BASE` and `MODEL_NAME`: optional service URL and model overrides.
- `CHAT_RATE_LIMIT`: per-client chat request limit; defaults to `20 per minute`.
- `RATELIMIT_STORAGE_URI`: defaults to in-memory storage for a single process. Use shared storage such as Redis with multiple workers.
- `CORS_ORIGINS`: optional comma-separated allowlist for a separately hosted frontend. Same-origin use needs no CORS configuration.
- `HOST` and `FLASK_DEBUG`: local defaults are `127.0.0.1` and debug off.

## Tests

From `backend/MutakamelaAI`:

```sh
PYTHONPATH=. python -m unittest discover -s . -p 'test_web_chat.py' -v
```

## Deployment

The built-in Flask server is for local development only. Deploy behind a production WSGI server, keep debug mode disabled, and configure shared rate-limit storage for multi-worker deployments.
