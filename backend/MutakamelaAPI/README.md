# Mutakamela Insurance API (.NET 8)

WhatsApp Business API integration with AI Policy Selector using .NET Web API.

## 🏗️ Project Structure

```
MutakamelaAPI/
├── Controllers/
│   ├── WebhookController.cs    # WhatsApp webhook endpoints
│   ├── ChatController.cs       # Direct chat API
│   └── HealthController.cs     # Health check
├── Services/
│   ├── WhatsAppService.cs      # WhatsApp Cloud API integration
│   ├── AIPolicyService.cs      # OpenAI-compatible AI integration
│   └── SessionManager.cs       # User session management
├── Models/
│   └── WhatsAppModels.cs       # DTOs for API
├── Data/
│   └── products.json           # Product catalog (copy from MutakamelaAI)
├── Program.cs
├── appsettings.json
└── MutakamelaAPI.csproj
```

## 🚀 Quick Start

### Prerequisites
- .NET 8 SDK
- Meta Business Account with WhatsApp API access

### 1. Clone and Navigate
```bash
cd backend/MutakamelaAPI
```

### 2. Copy Products Data
```bash
mkdir -p Data
cp ../../Data/products.json Data/
```

### 3. Start the Local AI Model
Install [Ollama](https://ollama.com), then download the model:

```bash
ollama pull qwen3:8b
```

### 4. Configure Settings
The default `appsettings.json` uses the local Ollama endpoint. Override these values with environment variables when needed:
```json
{
  "AI": {
    "BaseUrl": "http://localhost:11434/v1",
    "Model": "qwen3:8b"
  }
}
```

For production, set `AI__BaseUrl` to the private Ollama endpoint reachable by the API and `AI__Model` to the model installed there. `AI__ApiKey` is optional and only needed if the configured endpoint requires bearer authentication.

### 5. Run the API
```bash
dotnet restore
dotnet run
```

### 5. Test Endpoints

**Swagger UI:** https://localhost:5001/swagger

**Health Check:**
```bash
curl https://localhost:5001/api/health
```

**Direct Chat (without WhatsApp):**
```bash
curl -X POST https://localhost:5001/api/chat \
  -H "Content-Type: application/json" \
  -d '{"message": "I need car insurance"}'
```

## 📱 WhatsApp Setup

### 1. Expose with ngrok
```bash
ngrok http 5000
```

### 2. Configure Webhook in Meta Dashboard
- **Callback URL:** `https://xxx.ngrok.io/api/webhook`
- **Verify Token:** `mutakamela_verify_2024`
- Subscribe to: `messages`

## 🔌 API Endpoints

| Method | Endpoint | Description |
|--------|----------|-------------|
| GET | `/api/webhook` | WhatsApp webhook verification |
| POST | `/api/webhook` | Receive WhatsApp messages |
| POST | `/api/chat` | Direct chat (testing) |
| DELETE | `/api/chat/{sessionId}` | Clear session |
| GET | `/api/health` | Health check |

## 🏭 Production Deployment

### Azure App Service
```bash
dotnet publish -c Release -o ./publish
az webapp up --name mutakamela-api --resource-group your-rg
```

### Docker
```dockerfile
FROM mcr.microsoft.com/dotnet/aspnet:8.0
WORKDIR /app
COPY publish/ .
ENTRYPOINT ["dotnet", "MutakamelaAPI.dll"]
```

### IIS
1. Publish: `dotnet publish -c Release`
2. Create IIS site pointing to publish folder
3. Install ASP.NET Core Hosting Bundle

## 🔐 Environment Variables (Production)

```bash
WhatsApp__AccessToken=EAAxxxxxxx
WhatsApp__PhoneNumberId=123456789
AI__BaseUrl=http://localhost:11434/v1
AI__Model=qwen3:8b
# Optional for a secured remote AI endpoint:
AI__ApiKey=your-ai-provider-key
```

## 📝 Sample Conversation

```
User: My car had an accident
AI: I recommend Motor Insurance for your vehicle...
    📦 Coverage: Third Party, Own Damage, Theft
    🇸🇦 أوصي بتأمين المركبات لسيارتك...

User: Yes, tell me more
AI: Motor Insurance provides comprehensive protection...
```

## Agentic portal engine (Phase 2)

The API includes an agentic engine that walks a customer through the Mutakamela
e-services portal journeys from chat:

| Flow id | Portal page | Kind |
|---|---|---|
| `buy-insurance` | `/myInsurance/buy-insurance` | product selection → hands off to motor |
| `buy-motor-insurance` | `/myInsurance/buy-Motorinsurance` | write, stops at payment (never automated) |
| `personal-info` | `/myInsurance/personalinfo` | write with OTP + approval |
| `make-a-claim` | `/myInsurance/make-a-claim` | write with approval |
| `track-a-claim` | `/myInsurance/Track-a-Claim` | read-only |

### How it works

```
chat message ─▶ PortalIntent.Detect ─▶ ApplicationOrchestrator (state machine)
                                          │  Collecting → Validating → AwaitingLogin → Filling
                                          │  → AwaitingApproval → Submitting → Done | Failed | Cancelled
                                          ├─ RulesEngine      (Rules/motor.json — deterministic validation)
                                          ├─ FlowRegistry     (Browser/Flows/*.json — screens, fields, selectors)
                                          ├─ IBrowserAgent    (Simulated by default; Playwright when verified)
                                          └─ IApplicationJobStore (App_Data/jobs/*.json — survives restarts)
```

- The LLM is never consulted for a job step. Intent detection, validation, the
  state machine and the submit gate are all code.
- A job only reaches `Submitting` after the customer replies **confirm** on the
  review card (or `POST /api/applications/{id}/approve`). A second approve is a
  no-op: submits are keyed by `IdempotencyKey`.
- Card numbers, CVV and passwords are rejected on input and never stored. OTP
  codes are used once and stripped before the job is written to disk.
- Login is the customer's: the engine pauses in `AwaitingLogin` and resumes when
  they reply "logged in". Payment is a hand-off link; the agent stops at the
  payment screen.

### Configuration (`appsettings.json`)

```json
"Agent": {
  "Enabled": true,
  "Browser": "simulated",          // "playwright" once flows are verified and the package is installed
  "DataDir": "App_Data/jobs",
  "EvidenceDir": "App_Data/evidence"
}
```

Set `Agent:Enabled` to `false` to restore the previous chat-only behaviour.

### Endpoints

| Method | Path | Purpose |
|---|---|---|
| GET | `/api/applications/flows` | journeys and their fields |
| GET | `/api/applications?sessionId=` | list jobs |
| GET | `/api/applications/{id}` | job status, missing fields, review card, events |
| POST | `/api/applications` | start a job (`sessionId`, `flowId`, optional `data`) |
| PATCH | `/api/applications/{id}/data` | set/replace field values |
| POST | `/api/applications/{id}/login-complete` | customer finished portal login |
| POST | `/api/applications/{id}/approve` | explicit submit approval |
| POST | `/api/applications/{id}/cancel` | cancel |

Chat responses now carry an `application` object whenever a job is active, and
the stage is one of `APP_COLLECT`, `APP_LOGIN`, `APP_OTP`, `APP_REVIEW`,
`APP_PAYMENT`, `APP_DONE`, `APP_FAILED`, `APP_CANCELLED`. The web widget renders a
status/review card and the matching quick replies.

### Going live with the real browser agent

1. Capture each portal page (both languages) while logged in; record selectors
   and validation messages.
2. Update the matching `Browser/Flows/*.json`: replace placeholder `selectors`,
   confirm field ids/types, then set `"verified": true`.
3. Add `Microsoft.Playwright` to the project, run `playwright install chromium`,
   and implement `PlaywrightBrowserAgent` against the sketch in that file.
4. Set `Agent:Browser` to `playwright`. Unverified flows are refused at runtime.

### Tests

`backend/MutakamelaAPI.Tests` (xUnit) covers the rules engine, the job state
machine and all five journeys end to end with the simulated agent:

```bash
cd backend && DOTNET_ROLL_FORWARD=Major dotnet test MutakamelaAPI.Tests
```
