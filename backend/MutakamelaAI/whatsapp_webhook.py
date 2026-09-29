"""
Mutakamela Insurance - WhatsApp Integration
============================================
Connects WhatsApp Business API with AI Policy Selector

Setup:
    1. Set environment variables in .env
    2. pip install flask requests python-dotenv
    3. Run: python whatsapp_webhook.py
    4. Expose via ngrok: ngrok http 5000
    5. Configure webhook URL in Meta Dashboard
"""

import os
import json
import requests
from flask import Flask, request, jsonify
from dotenv import load_dotenv
from policy_selector_adesso import PolicySelectorAdesso

load_dotenv()

app = Flask(__name__)

# WhatsApp API Configuration
WHATSAPP_TOKEN = os.getenv("WHATSAPP_TOKEN")
WHATSAPP_PHONE_ID = os.getenv("WHATSAPP_PHONE_ID")
VERIFY_TOKEN = os.getenv("WEBHOOK_VERIFY_TOKEN", "mutakamela_verify_2024")

# WhatsApp API URL
WHATSAPP_API_URL = f"https://graph.facebook.com/v18.0/{WHATSAPP_PHONE_ID}/messages"

# Store user sessions (in production, use Redis or database)
user_sessions = {}


def get_or_create_session(phone_number: str) -> PolicySelectorAdesso:
    """Get existing session or create new one for user"""
    if phone_number not in user_sessions:
        user_sessions[phone_number] = PolicySelectorAdesso()
        print(f"📱 New session created for: {phone_number}")
    return user_sessions[phone_number]


def send_whatsapp_message(to: str, message: str):
    """Send message via WhatsApp Cloud API"""
    headers = {
        "Authorization": f"Bearer {WHATSAPP_TOKEN}",
        "Content-Type": "application/json"
    }

    payload = {
        "messaging_product": "whatsapp",
        "recipient_type": "individual",
        "to": to,
        "type": "text",
        "text": {"body": message}
    }

    try:
        response = requests.post(WHATSAPP_API_URL, headers=headers, json=payload)
        response.raise_for_status()
        print(f"✅ Message sent to {to}")
        return True
    except Exception as e:
        print(f"❌ Failed to send message: {e}")
        return False


def send_whatsapp_interactive(to: str, body: str, buttons: list):
    """Send interactive message with buttons"""
    headers = {
        "Authorization": f"Bearer {WHATSAPP_TOKEN}",
        "Content-Type": "application/json"
    }

    button_list = []
    for i, btn in enumerate(buttons[:3]):  # Max 3 buttons
        button_list.append({
            "type": "reply",
            "reply": {
                "id": f"btn_{i}",
                "title": btn[:20]  # Max 20 chars
            }
        })

    payload = {
        "messaging_product": "whatsapp",
        "recipient_type": "individual",
        "to": to,
        "type": "interactive",
        "interactive": {
            "type": "button",
            "body": {"text": body},
            "action": {"buttons": button_list}
        }
    }

    try:
        response = requests.post(WHATSAPP_API_URL, headers=headers, json=payload)
        response.raise_for_status()
        return True
    except Exception as e:
        print(f"❌ Interactive message failed: {e}")
        # Fallback to text
        return send_whatsapp_message(to, body)


def process_user_message(phone_number: str, message_text: str) -> str:
    """Process incoming message through AI Policy Selector"""

    # Get user's AI session
    selector = get_or_create_session(phone_number)

    # Check for reset command
    if message_text.lower() in ['reset', 'start over', 'clear', 'new']:
        selector.clear_history()
        return "🔄 Session cleared! How can I help you with insurance today?"

    # Process through AI
    try:
        result = selector.select_policy(message_text)

        # Build response
        response_parts = []

        # Main response
        if result.get('response'):
            response_parts.append(result['response'])

        # Product details
        details = result.get('product_details', {})
        if details.get('coverage'):
            response_parts.append(f"\n📦 Coverage: {', '.join(details['coverage'][:3])}")
        if details.get('key_benefits'):
            response_parts.append(f"⭐ Benefits: {', '.join(details['key_benefits'][:3])}")

        # Arabic response
        if result.get('response_ar'):
            response_parts.append(f"\n🇸🇦 {result['response_ar']}")

        return "\n".join(response_parts)

    except Exception as e:
        print(f"❌ AI Error: {e}")
        return "I'm sorry, I encountered an issue. Please try again or type 'reset' to start over."


# ============ WEBHOOK ENDPOINTS ============

@app.route('/webhook', methods=['GET'])
def verify_webhook():
    """Webhook verification for Meta"""
    mode = request.args.get('hub.mode')
    token = request.args.get('hub.verify_token')
    challenge = request.args.get('hub.challenge')

    if mode == 'subscribe' and token == VERIFY_TOKEN:
        print("✅ Webhook verified!")
        return challenge, 200
    else:
        print("❌ Webhook verification failed")
        return 'Forbidden', 403


@app.route('/webhook', methods=['POST'])
def receive_message():
    """Handle incoming WhatsApp messages"""
    try:
        data = request.get_json()

        # Extract message details
        entry = data.get('entry', [{}])[0]
        changes = entry.get('changes', [{}])[0]
        value = changes.get('value', {})
        messages = value.get('messages', [])

        if not messages:
            return jsonify({"status": "no message"}), 200

        message = messages[0]
        phone_number = message.get('from')
        message_type = message.get('type')

        # Extract text based on message type
        if message_type == 'text':
            message_text = message.get('text', {}).get('body', '')
        elif message_type == 'interactive':
            # Button reply
            interactive = message.get('interactive', {})
            if interactive.get('type') == 'button_reply':
                message_text = interactive.get('button_reply', {}).get('title', '')
            else:
                message_text = interactive.get('list_reply', {}).get('title', '')
        else:
            message_text = ''

        if not message_text:
            return jsonify({"status": "unsupported message type"}), 200

        print(f"📩 Received from {phone_number}: {message_text}")

        # Process through AI
        ai_response = process_user_message(phone_number, message_text)

        # Send response
        send_whatsapp_message(phone_number, ai_response)

        return jsonify({"status": "success"}), 200

    except Exception as e:
        print(f"❌ Webhook error: {e}")
        return jsonify({"status": "error", "message": str(e)}), 500


@app.route('/health', methods=['GET'])
def health_check():
    """Health check endpoint"""
    return jsonify({
        "status": "healthy",
        "service": "Mutakamela WhatsApp Integration",
        "active_sessions": len(user_sessions)
    })


@app.route('/send-test', methods=['POST'])
def send_test():
    """Test endpoint to send a message"""
    data = request.get_json()
    phone = data.get('phone')
    message = data.get('message', 'Hello from Mutakamela Insurance!')

    if not phone:
        return jsonify({"error": "phone required"}), 400

    success = send_whatsapp_message(phone, message)
    return jsonify({"success": success})


if __name__ == '__main__':
    print("\n" + "="*60)
    print("🏢 MUTAKAMELA INSURANCE - WhatsApp Integration")
    print("="*60)
    print(f"📱 Phone ID: {WHATSAPP_PHONE_ID or 'NOT SET'}")
    print(f"🔑 Token: {'SET' if WHATSAPP_TOKEN else 'NOT SET'}")
    print(f"✅ Verify Token: {VERIFY_TOKEN}")
    print("\nEndpoints:")
    print("  GET  /webhook  - Webhook verification")
    print("  POST /webhook  - Receive messages")
    print("  GET  /health   - Health check")
    print("  POST /send-test - Send test message")
    print("\n⚠️  Use ngrok for public URL: ngrok http 5000")
    print("="*60 + "\n")

    app.run(host='0.0.0.0', port=5000, debug=True)
