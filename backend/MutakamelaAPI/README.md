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
cp ../MutakamelaAI/Data/products.json Data/
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
