# WhatsApp Integration Setup Guide
## Mutakamela Insurance AI Chatbot

---

## 📋 Prerequisites

- Meta Business Account
- Business Phone Number (not personal WhatsApp)
- Python 3.8+
- ngrok (for local testing)

---

## 🚀 Step-by-Step Setup

### Step 1: Create Meta Developer App

1. Go to [developers.facebook.com](https://developers.facebook.com)
2. Click **My Apps** → **Create App**
3. Select **Business** type
4. Enter App name: `Mutakamela Insurance Bot`
5. Click **Create App**

### Step 2: Add WhatsApp Product

1. In your app dashboard, click **Add Products**
2. Find **WhatsApp** and click **Set Up**
3. You'll be directed to WhatsApp API Setup

### Step 3: Get API Credentials

From **WhatsApp → API Setup** page, note down:

```
Phone Number ID: ________________
WhatsApp Business Account ID: ________________
Temporary Access Token: ________________
```

### Step 4: Configure Environment

Edit `.env` file:

```bash
cd backend/MutakamelaAI
nano .env
```

Add your credentials:

```env
WHATSAPP_TOKEN=EAAxxxxxxxxxxxxxxxxx
WHATSAPP_PHONE_ID=123456789012345
WEBHOOK_VERIFY_TOKEN=mutakamela_verify_2024
```

### Step 5: Install Dependencies

```bash
pip install flask requests python-dotenv
```

### Step 6: Start the Webhook Server

```bash
cd backend/MutakamelaAI
python whatsapp_webhook.py
```

You should see:
```
🏢 MUTAKAMELA INSURANCE - WhatsApp Integration
📱 Phone ID: 123456789012345
🔑 Token: SET
✅ Verify Token: mutakamela_verify_2024
```

### Step 7: Expose with ngrok (Local Testing)

In a new terminal:

```bash
ngrok http 5000
```

Copy the HTTPS URL, e.g., `https://abc123.ngrok.io`

### Step 8: Configure Webhook in Meta Dashboard

1. Go to **WhatsApp → Configuration**
2. Click **Edit** under Webhook
3. Enter:
   - **Callback URL**: `https://abc123.ngrok.io/webhook`
   - **Verify Token**: `mutakamela_verify_2024`
4. Click **Verify and Save**
5. Subscribe to: `messages`

### Step 9: Test the Integration

1. Go to **WhatsApp → API Setup**
2. Under "Send and receive messages", click **Send Message**
3. Send a test message to your WhatsApp number
4. Reply with: `I need car insurance`
5. The AI should respond!

---

## 🏗️ Production Deployment

### Option A: Deploy to Cloud (Recommended)

**Railway.app:**
```bash
# Install Railway CLI
npm install -g @railway/cli

# Login and deploy
railway login
railway init
railway up
```

**Heroku:**
```bash
heroku create mutakamela-whatsapp
git push heroku main
```

**AWS/GCP/Azure:**
- Use App Service, Cloud Run, or EC2
- Set environment variables in cloud console

### Option B: VPS Deployment

```bash
# On your server
git clone your-repo
cd backend/MutakamelaAI
pip install -r requirements.txt

# Run with gunicorn
gunicorn -w 4 -b 0.0.0.0:5000 whatsapp_webhook:app

# Or use PM2
pm2 start whatsapp_webhook.py --interpreter python3
```

### SSL Certificate (Required for Production)

Use Let's Encrypt:
```bash
sudo certbot --nginx -d your-domain.com
```

---

## 📱 WhatsApp Message Flow

```
┌─────────────────────────────────────────────────────────────┐
│                    USER SENDS MESSAGE                        │
│              "My car had an accident"                        │
└─────────────────────────────────────────────────────────────┘
                            │
                            ▼
┌─────────────────────────────────────────────────────────────┐
│                   WhatsApp Cloud API                         │
│              Sends webhook to your server                    │
└─────────────────────────────────────────────────────────────┘
                            │
                            ▼
┌─────────────────────────────────────────────────────────────┐
│                  Flask Webhook Server                        │
│              POST /webhook receives message                  │
└─────────────────────────────────────────────────────────────┘
                            │
                            ▼
┌─────────────────────────────────────────────────────────────┐
│              Mutakamela AI Policy Selector                   │
│         - Intent: BUY_POLICY                                 │
│         - LOB: MOTOR                                         │
│         - Recommended: Motor Insurance                       │
└─────────────────────────────────────────────────────────────┘
                            │
                            ▼
┌─────────────────────────────────────────────────────────────┐
│                   WhatsApp Cloud API                         │
│              Sends response to user                          │
└─────────────────────────────────────────────────────────────┘
                            │
                            ▼
┌─────────────────────────────────────────────────────────────┐
│                    USER RECEIVES                             │
│  "I recommend Motor Insurance for your vehicle..."           │
│  📦 Coverage: Third Party, Own Damage, Theft                │
│  🇸🇦 أوصي بتأمين المركبات لسيارتك...                          │
└─────────────────────────────────────────────────────────────┘
```

---

## 🔐 Security Checklist

- [ ] Use HTTPS only (SSL certificate)
- [ ] Store tokens in environment variables
- [ ] Validate webhook signatures
- [ ] Rate limit incoming requests
- [ ] Log all interactions for audit
- [ ] Use permanent access token (not temporary)

---

## 🐛 Troubleshooting

| Issue | Solution |
|-------|----------|
| Webhook verification fails | Check VERIFY_TOKEN matches exactly |
| Messages not received | Ensure `messages` webhook is subscribed |
| 401 Unauthorized | Token expired, generate new one |
| ngrok URL changed | Update webhook URL in Meta Dashboard |
| AI not responding | Check adesso API key is valid |

---

## 📞 Support

- Meta Developer Support: [developers.facebook.com/support](https://developers.facebook.com/support)
- WhatsApp Business API Docs: [developers.facebook.com/docs/whatsapp](https://developers.facebook.com/docs/whatsapp)
