# Mutakamela AI Policy Selector

**Step 1 of AI Insurance Platform**: User types something → AI decides which policy (out of 44)

## Quick Start

```bash
# 1. Navigate to project
cd backend/MutakamelaAI

# 2. Install dependencies (optional - works without)
pip install openai python-dotenv

# 3. Set API key (optional - uses rule-based without it)
cp .env.example .env
# Edit .env and add your OPENAI_API_KEY

# 4. Run interactive demo
python policy_selector.py
```

## Web Chat

The Flask chatbot uses the Adesso API. Set `ADESSO_API_KEY` in `.env`, then run
`./run_chat.sh` from this directory. It binds to `127.0.0.1` with debug mode off
by default. Set `HOST` or `FLASK_DEBUG=true` only for a trusted development
environment.

Chat requests are limited to 20 per minute per client address by default. Set
`CHAT_RATE_LIMIT` to change the limit. The default in-memory rate-limit store is
for a single process; multi-worker deployments should set
`RATELIMIT_STORAGE_URI` to a shared store such as Redis. Same-origin requests do
not need CORS; set `CORS_ORIGINS` to a comma-separated allowlist only when using
a separate frontend origin.

## How It Works

```
User Input: "I need car insurance"
         ↓
┌─────────────────────────────────┐
│     Intent Classification       │
│  → BUY_POLICY                   │
└─────────────────────────────────┘
         ↓
┌─────────────────────────────────┐
│     Entity Extraction           │
│  → LOB: MOTOR                   │
│  → coverage: not specified      │
└─────────────────────────────────┘
         ↓
┌─────────────────────────────────┐
│     Policy Matching             │
│  → Motor TPL (85%)              │
│  → Motor Comprehensive (75%)    │
│  → Fleet Insurance (40%)        │
└─────────────────────────────────┘
         ↓
Output: Top 3 matching policies + follow-up question
```

## 44 Products Across 7 LOBs

| LOB | Products |
|-----|----------|
| MOTOR | TPL, Comprehensive (Silver/Gold/Platinum), Fleet, Motorcycle |
| HEALTH | Individual, Family, Corporate, VIP, Maternity, Dental |
| TRAVEL | Single Trip, Annual, Schengen, Hajj/Umrah, Student |
| PROPERTY | Home Basic/Comprehensive, Commercial, Industrial, Landlord |
| MARINE | Cargo, Hull, Freight Forwarder, Inland Transit |
| LIABILITY | Public, Professional, D&O, Product, Employer, Cyber |
| ENGINEERING | CAR, EAR, Machinery, Electronic, Decennial, Plant |

## Test Examples

```
I need car insurance              → Motor TPL
تأمين صحي لعائلتي                 → Family Health Insurance
travel to Europe                  → Schengen Visa Travel
insure my construction project    → Contractor All Risk
health for company employees      → Corporate Health Insurance
```

## Two Modes

1. **AI Mode** (with OpenAI API key)
   - Uses GPT-4o for accurate intent/entity extraction
   - Better Arabic understanding
   - Contextual responses

2. **Rule-Based Mode** (no API key needed)
   - Keyword matching
   - Works offline
   - Good for testing

## Files

```
MutakamelaAI/
├── policy_selector.py   # Main AI engine
├── Data/
│   └── products.json    # 44 insurance products
├── requirements.txt     # Dependencies
├── .env.example         # Config template
└── README.md
```

## Next Steps

1. ✅ Policy Selection AI (this)
2. ⬜ Add WhatsApp webhook integration
3. ⬜ Connect to rating engine for quotes
4. ⬜ Add conversation state management
5. ⬜ Integrate with backend APIs
