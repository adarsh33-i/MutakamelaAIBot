"""
Mutakamela Insurance AI - Interactive Web Chat
Branded chat interface matching mutakamela.sa design
"""

from flask import Flask, request, jsonify, render_template_string
from flask_cors import CORS
from flask_limiter import Limiter
from flask_limiter.util import get_remote_address
from policy_selector_adesso import PolicySelectorAdesso
import os
import re
import time
from dotenv import load_dotenv

load_dotenv()

app = Flask(__name__)
app.config['MAX_CONTENT_LENGTH'] = 128 * 1024

limiter = Limiter(
    get_remote_address,
    app=app,
    storage_uri=os.getenv('RATELIMIT_STORAGE_URI', 'memory://'),
    headers_enabled=True
)

cors_origins = [origin.strip() for origin in os.getenv('CORS_ORIGINS', '').split(',') if origin.strip()]
if cors_origins:
    CORS(app, origins=cors_origins)

# Session-based AI instances with timestamps (for cleanup)
sessions = {}
SESSION_TIMEOUT = 3600  # 1 hour in seconds
MAX_MESSAGE_LENGTH = 4000
MAX_HISTORY_MESSAGES = 20
SESSION_ID_PATTERN = re.compile(r'^(?:[0-9a-f]{32}|[0-9a-f]{8}-(?:[0-9a-f]{4}-){3}[0-9a-f]{12})$', re.IGNORECASE)

def cleanup_old_sessions(current_time=None):
    """Remove sessions older than SESSION_TIMEOUT"""
    current_time = current_time if current_time is not None else time.time()
    expired = [sid for sid, data in sessions.items()
               if current_time - data.get('last_accessed', data['created']) > SESSION_TIMEOUT]
    for sid in expired:
        del sessions[sid]
    if expired:
        print(f"🧹 Cleaned up {len(expired)} expired sessions")


def normalize_conversation_history(history):
    if not isinstance(history, list):
        raise ValueError('Conversation history must be a list')

    normalized = []
    for item in history[-MAX_HISTORY_MESSAGES:]:
        if not isinstance(item, dict):
            raise ValueError('Conversation history items must be objects')
        role = item.get('role')
        content = item.get('content')
        if role not in ('user', 'assistant') or not isinstance(content, str):
            raise ValueError('Conversation history contains an invalid message')
        content = content.strip()
        if not content or len(content) > MAX_MESSAGE_LENGTH:
            raise ValueError('Conversation history message length is invalid')
        normalized.append({'role': role, 'content': content})
    return normalized

# Mutakamela Branded Chat HTML Template
CHAT_HTML = '''
<!DOCTYPE html>
<html lang="en">
<head>
    <meta charset="UTF-8">
    <meta name="viewport" content="width=device-width, initial-scale=1.0">
    <title>Mutakamela Insurance - AI Assistant</title>
    <link href="https://fonts.googleapis.com/css2?family=Open+Sans:wght@300;400;500;600;700&display=swap" rel="stylesheet">
    <link rel="stylesheet" href="https://cdnjs.cloudflare.com/ajax/libs/font-awesome/6.4.0/css/all.min.css">
    <style>
        :root {
            --primary: #0066B3;
            --primary-dark: #004d86;
            --primary-light: #e6f3ff;
            --secondary: #00A0DC;
            --accent: #F7941D;
            --success: #28a745;
            --text-dark: #1a1a1a;
            --text-gray: #666666;
            --text-light: #999999;
            --bg-light: #f5f7fa;
            --bg-white: #ffffff;
            --border: #e0e6ed;
        }

        * {
            margin: 0;
            padding: 0;
            box-sizing: border-box;
        }

        body {
            font-family: 'Open Sans', sans-serif;
            background: linear-gradient(135deg, #0066B3 0%, #004d86 100%);
            min-height: 100vh;
            display: flex;
            justify-content: center;
            align-items: center;
            padding: 20px;
        }

        .chat-container {
            width: 100%;
            max-width: 450px;
            height: 90vh;
            max-height: 750px;
            background: var(--bg-white);
            border-radius: 16px;
            box-shadow: 0 20px 60px rgba(0,0,0,0.3);
            display: flex;
            flex-direction: column;
            overflow: hidden;
        }

        /* Header - Mutakamela Style */
        .chat-header {
            background: linear-gradient(135deg, var(--primary) 0%, var(--primary-dark) 100%);
            color: white;
            padding: 20px;
            position: relative;
        }

        .header-top {
            display: flex;
            align-items: center;
            gap: 15px;
            margin-bottom: 12px;
        }

        .logo-container {
            width: 50px;
            height: 50px;
            background: white;
            border-radius: 12px;
            display: flex;
            align-items: center;
            justify-content: center;
            box-shadow: 0 4px 15px rgba(0,0,0,0.2);
            padding: 5px;
        }

        .logo-container .logo-img {
            width: 100%;
            height: 100%;
        }

        .header-info h1 {
            font-size: 1.2rem;
            font-weight: 700;
            margin-bottom: 2px;
        }

        .header-info p {
            font-size: 0.8rem;
            opacity: 0.9;
        }

        .header-bottom {
            display: flex;
            justify-content: space-between;
            align-items: center;
        }

        .status-badge {
            display: inline-flex;
            align-items: center;
            gap: 6px;
            background: rgba(255,255,255,0.2);
            padding: 6px 12px;
            border-radius: 20px;
            font-size: 0.75rem;
        }

        .status-dot {
            width: 8px;
            height: 8px;
            background: #4ade80;
            border-radius: 50%;
            animation: pulse-dot 2s infinite;
        }

        @keyframes pulse-dot {
            0%, 100% { opacity: 1; }
            50% { opacity: 0.5; }
        }

        .lang-toggle {
            display: flex;
            gap: 5px;
        }

        .lang-btn {
            padding: 6px 12px;
            border: 1px solid rgba(255,255,255,0.4);
            background: transparent;
            color: white;
            border-radius: 6px;
            cursor: pointer;
            font-size: 0.75rem;
            font-weight: 600;
            transition: all 0.2s;
        }

        .lang-btn:hover, .lang-btn.active {
            background: white;
            color: var(--primary);
        }

        /* Chat Messages Area */
        .chat-messages {
            flex: 1;
            overflow-y: auto;
            padding: 20px;
            background: var(--bg-light);
        }

        .chat-messages::-webkit-scrollbar {
            width: 5px;
        }

        .chat-messages::-webkit-scrollbar-thumb {
            background: var(--border);
            border-radius: 3px;
        }

        .message {
            margin-bottom: 16px;
            display: flex;
            gap: 10px;
            animation: fadeIn 0.3s ease;
        }

        @keyframes fadeIn {
            from { opacity: 0; transform: translateY(8px); }
            to { opacity: 1; transform: translateY(0); }
        }

        .message.user {
            flex-direction: row-reverse;
        }

        .message-avatar {
            width: 36px;
            height: 36px;
            border-radius: 10px;
            display: flex;
            align-items: center;
            justify-content: center;
            font-size: 14px;
            flex-shrink: 0;
        }

        .message.bot .message-avatar {
            background: var(--primary);
            color: white;
        }

        .message.user .message-avatar {
            background: var(--accent);
            color: white;
        }

        .message-content {
            max-width: 80%;
        }

        .message-bubble {
            padding: 12px 16px;
            border-radius: 16px;
            font-size: 0.9rem;
            line-height: 1.5;
        }

        .message.user .message-bubble {
            background: var(--primary);
            color: white;
            border-bottom-right-radius: 4px;
        }

        .message.bot .message-bubble {
            background: white;
            color: var(--text-dark);
            border-bottom-left-radius: 4px;
            box-shadow: 0 2px 8px rgba(0,0,0,0.06);
        }

        .message-time {
            font-size: 0.7rem;
            color: var(--text-light);
            margin-top: 4px;
            display: block;
        }

        .message.user .message-time {
            text-align: right;
        }

        /* Typing Indicator */
        .typing-indicator {
            display: flex;
            gap: 4px;
            padding: 12px 16px;
            background: white;
            border-radius: 16px;
            width: fit-content;
            margin-left: 46px;
            box-shadow: 0 2px 8px rgba(0,0,0,0.06);
        }

        .typing-indicator span {
            width: 8px;
            height: 8px;
            background: var(--primary);
            border-radius: 50%;
            animation: typing 1.4s infinite;
        }

        .typing-indicator span:nth-child(2) { animation-delay: 0.2s; }
        .typing-indicator span:nth-child(3) { animation-delay: 0.4s; }

        @keyframes typing {
            0%, 100% { transform: scale(0.8); opacity: 0.4; }
            50% { transform: scale(1.1); opacity: 1; }
        }

        /* Product Card */
        .product-card {
            background: linear-gradient(135deg, var(--primary-light) 0%, #fff 100%);
            border-radius: 12px;
            padding: 14px;
            margin-top: 12px;
            border-left: 4px solid var(--primary);
        }

        .product-card h4 {
            color: var(--primary);
            font-size: 0.95rem;
            font-weight: 600;
            margin-bottom: 8px;
            display: flex;
            align-items: center;
            gap: 8px;
        }

        .product-card ul {
            margin: 0;
            padding-left: 18px;
            font-size: 0.8rem;
            color: var(--text-gray);
        }

        .product-card li {
            margin: 4px 0;
        }

        /* Arabic Text */
        .bilingual {
            margin-top: 10px;
            padding-top: 10px;
            border-top: 1px dashed var(--border);
            direction: rtl;
            text-align: right;
            color: var(--text-gray);
            font-size: 0.85rem;
        }

        /* Quote Form */
        .quote-form {
            background: var(--primary-light);
            border-radius: 12px;
            padding: 15px;
            margin-top: 12px;
        }

        .quote-form h4 {
            color: var(--primary);
            font-size: 0.9rem;
            margin-bottom: 12px;
            display: flex;
            align-items: center;
            gap: 8px;
        }

        .quote-form input {
            width: 100%;
            padding: 10px 12px;
            border: 1px solid var(--border);
            border-radius: 8px;
            margin-bottom: 8px;
            font-size: 0.85rem;
            font-family: 'Open Sans', sans-serif;
        }

        .quote-form input:focus {
            outline: none;
            border-color: var(--primary);
        }

        .quote-form button {
            width: 100%;
            padding: 10px;
            background: var(--primary);
            color: white;
            border: none;
            border-radius: 8px;
            font-weight: 600;
            cursor: pointer;
            transition: background 0.2s;
        }

        .quote-form button:hover {
            background: var(--primary-dark);
        }

        .quote-form button:disabled,
        .send-btn:disabled {
            cursor: wait;
            opacity: 0.65;
        }

        .quote-error {
            color: #b42318;
            font-size: 0.8rem;
            margin-top: 8px;
        }

        body.dark-mode .quote-error {
            color: #ff8a80;
        }

        /* Price Tag */
        .price-tag {
            display: inline-block;
            background: var(--accent);
            color: white;
            padding: 4px 10px;
            border-radius: 12px;
            font-size: 0.75rem;
            font-weight: 600;
            margin-top: 8px;
        }

        /* Quick Replies */
        .quick-replies {
            display: flex;
            flex-wrap: wrap;
            gap: 8px;
            margin-top: 12px;
        }

        .quick-reply {
            padding: 8px 14px;
            background: white;
            border: 2px solid var(--primary);
            color: var(--primary);
            border-radius: 20px;
            font-size: 0.8rem;
            font-weight: 600;
            cursor: pointer;
            transition: all 0.2s;
            display: flex;
            align-items: center;
            gap: 5px;
        }

        .quick-reply:hover {
            background: var(--primary);
            color: white;
        }

        /* Category Buttons */
        .category-section {
            padding: 8px 12px;
            background: white;
            border-top: 1px solid var(--border);
        }

        .category-label {
            font-size: 0.65rem;
            color: var(--text-light);
            text-transform: uppercase;
            letter-spacing: 0.5px;
            margin-bottom: 6px;
            display: flex;
            align-items: center;
            gap: 5px;
        }

        .category-label::before {
            content: '';
            width: 10px;
            height: 2px;
            background: var(--primary);
            border-radius: 2px;
        }

        .category-buttons {
            display: grid;
            grid-template-columns: repeat(5, 1fr);
            gap: 6px;
        }

        .category-btn {
            display: flex;
            flex-direction: column;
            align-items: center;
            gap: 4px;
            padding: 10px 6px;
            background: linear-gradient(145deg, #ffffff, #f5f7fa);
            border: 1.5px solid transparent;
            border-radius: 10px;
            cursor: pointer;
            transition: all 0.3s cubic-bezier(0.4, 0, 0.2, 1);
            position: relative;
            overflow: hidden;
        }

        .category-btn::before {
            content: '';
            position: absolute;
            top: 0;
            left: 0;
            right: 0;
            bottom: 0;
            background: linear-gradient(135deg, var(--primary) 0%, var(--secondary) 100%);
            opacity: 0;
            transition: opacity 0.3s;
            z-index: 0;
        }

        .category-btn:hover {
            transform: translateY(-4px);
            border-color: var(--primary);
            box-shadow: 0 8px 25px rgba(0, 102, 179, 0.25);
        }

        .category-btn:hover::before {
            opacity: 1;
        }

        .category-btn:hover .icon,
        .category-btn:hover .label {
            color: white;
        }

        .category-btn:active {
            transform: translateY(-2px) scale(0.98);
        }

        .category-btn .icon {
            font-size: 1.1rem;
            position: relative;
            z-index: 1;
            transition: all 0.3s;
        }

        .category-btn .label {
            font-size: 0.65rem;
            font-weight: 600;
            color: var(--text-dark);
            position: relative;
            z-index: 1;
            transition: all 0.3s;
        }

        .category-btn .icon-bg {
            width: 32px;
            height: 32px;
            border-radius: 8px;
            display: flex;
            align-items: center;
            justify-content: center;
            transition: all 0.3s;
        }

        .category-btn.motor .icon-bg { background: #fff3e0; }
        .category-btn.motor .icon { color: #F7941D; }
        .category-btn.health .icon-bg { background: #e8f5e9; }
        .category-btn.health .icon { color: #43a047; }
        .category-btn.travel .icon-bg { background: #e0f2f1; }
        .category-btn.travel .icon { color: #00897b; }
        .category-btn.property .icon-bg { background: #fff8e1; }
        .category-btn.property .icon { color: #ff8f00; }
        .category-btn.claims .icon-bg { background: #fce4ec; }
        .category-btn.claims .icon { color: #e91e63; }

        .category-btn:hover .icon-bg {
            background: rgba(255,255,255,0.2);
        }

        /* Ripple effect */
        .category-btn .ripple {
            position: absolute;
            border-radius: 50%;
            background: rgba(255,255,255,0.4);
            transform: scale(0);
            animation: ripple 0.6s linear;
            pointer-events: none;
        }

        @keyframes ripple {
            to {
                transform: scale(4);
                opacity: 0;
            }
        }

        /* Chat Input */
        .chat-input {
            padding: 15px;
            background: white;
            border-top: 1px solid var(--border);
            display: flex;
            gap: 10px;
            align-items: center;
        }

        .chat-input input {
            flex: 1;
            padding: 14px 18px;
            border: 2px solid var(--border);
            border-radius: 12px;
            font-size: 0.9rem;
            font-family: 'Open Sans', sans-serif;
            outline: none;
            transition: border-color 0.2s;
        }

        .chat-input input:focus {
            border-color: var(--primary);
        }

        .chat-input input::placeholder {
            color: var(--text-light);
        }

        .send-btn {
            width: 50px;
            height: 50px;
            border: none;
            background: var(--primary);
            color: white;
            border-radius: 12px;
            cursor: pointer;
            font-size: 1.1rem;
            transition: all 0.2s;
            display: flex;
            align-items: center;
            justify-content: center;
        }

        .send-btn:hover {
            background: var(--primary-dark);
            transform: scale(1.05);
        }

        /* Footer */
        .chat-footer {
            padding: 8px;
            background: var(--bg-light);
            text-align: center;
            font-size: 0.7rem;
            color: var(--text-light);
            position: relative;
        }

        .chat-footer a {
            color: var(--primary);
            text-decoration: none;
        }

        .theme-toggle {
            position: absolute;
            right: 10px;
            background: transparent;
            border: none;
            color: var(--text-light);
            cursor: pointer;
            font-size: 0.9rem;
            padding: 5px;
            transition: color 0.3s;
        }

        .theme-toggle:hover {
            color: var(--primary);
        }

        /* Dark Mode */
        body.dark-mode {
            background: linear-gradient(135deg, #1a1a2e 0%, #16213e 100%);
        }

        body.dark-mode .chat-container {
            background: #1a1a2e;
            box-shadow: 0 20px 60px rgba(0,0,0,0.5);
        }

        body.dark-mode .chat-messages {
            background: #16213e;
        }

        body.dark-mode .message.bot .message-bubble {
            background: #1f2940;
            color: #e0e0e0;
        }

        body.dark-mode .category-section {
            background: #1a1a2e;
            border-color: #2a2a4e;
        }

        body.dark-mode .category-btn {
            background: linear-gradient(145deg, #1f2940, #16213e);
        }

        body.dark-mode .category-btn .label {
            color: #e0e0e0;
        }

        body.dark-mode .chat-input {
            background: #1a1a2e;
            border-color: #2a2a4e;
        }

        body.dark-mode .chat-input input {
            background: #16213e;
            border-color: #2a2a4e;
            color: #e0e0e0;
        }

        body.dark-mode .chat-footer {
            background: #16213e;
            color: #888;
        }

        body.dark-mode .product-card {
            background: linear-gradient(135deg, #1f2940 0%, #16213e 100%);
        }

        body.dark-mode .quick-reply {
            background: #1f2940;
            border-color: var(--primary);
            color: #e0e0e0;
        }

        body.dark-mode .quote-form {
            background: #1f2940;
        }

        body.dark-mode .quote-form input {
            background: #16213e;
            border-color: #2a2a4e;
            color: #e0e0e0;
        }

        body.dark-mode .quote-form h4 {
            color: var(--secondary);
        }

        body.dark-mode .bilingual {
            border-color: #2a2a4e;
            color: #aaa;
        }

        body.dark-mode .theme-toggle i::before {
            content: "\f185";
        }

        /* Responsive */
        @media (max-width: 480px) {
            body { padding: 0; }
            .chat-container {
                height: 100vh;
                max-height: none;
                border-radius: 0;
            }
            .category-buttons {
                grid-template-columns: repeat(2, minmax(0, 1fr));
            }
            .category-btn .label {
                font-size: 0.6rem;
            }
            .category-btn .icon-bg {
                width: 28px;
                height: 28px;
            }
        }
    </style>
</head>
<body>
    <div class="chat-container">
        <div class="chat-header">
            <div class="header-top">
                <div class="logo-container">
                    <svg class="logo-img" viewBox="0 0 55 55" xmlns="http://www.w3.org/2000/svg">
                        <path d="M0.772116 16.3861C5.942 23.861 21.0231 53.3243 31.7953 53.3243C39.9883 53.3243 53.3474 18.5753 53.3474 10.2317C53.3358 -9.5637 -7.42866 3.18919 0.772116 16.3861ZM18.9613 42.7954C14.8995 37.2896 8.03467 8.7722 12.9845 4.63321C19.2779 -0.413125 36.8764 8.08494 46.332 12.9923C56.3706 18.2008 27.4787 55.6486 18.9381 42.7954" fill="#0C72E8"/>
                        <path d="M25.413 5.40541L24.6408 5.01931C14.8378 1.18147 11.8185 35.6023 19.5752 34.834C24.6563 34.3282 41.6563 32.3475 43.6795 26.7259C44.6177 22.3668 29.7528 7.53668 25.413 5.40541ZM32.4324 30.2471C24.1351 29.4093 15.83 29.1236 16.0269 25.5792C16.5289 20.4402 24.4864 10.2008 28.749 10.7838C34.2625 11.556 36.471 23.112 36.5791 28.4672C36.6215 30.4633 34.3629 30.5405 32.4324 30.2471Z" fill="#2E289E"/>
                    </svg>
                </div>
                <div class="header-info">
                    <h1>Mutakamela Insurance</h1>
                    <p>AI-Powered Assistant</p>
                </div>
            </div>
            <div class="header-bottom">
                <div class="status-badge">
                    <span class="status-dot"></span>
                    Online
                </div>
                <div class="lang-toggle">
                    <button class="lang-btn new-chat-action" onclick="startNewChat()" title="New Chat" data-i18n-title="newChat" style="padding: 6px 10px;">
                        <i class="fas fa-redo"></i>
                    </button>
                    <button class="lang-btn active" data-language="en" onclick="setLang('en', this)">EN</button>
                    <button class="lang-btn" data-language="ar" onclick="setLang('ar', this)">عربي</button>
                </div>
            </div>
        </div>

        <div class="chat-messages" id="chatMessages">
            <div class="message bot">
                <div class="message-avatar"><i class="fas fa-headset"></i></div>
                <div class="message-content">
                    <div class="message-bubble">
                        <strong>Welcome to Mutakamela Insurance!</strong> 👋<br><br>
                        I'm here to help you find the right coverage. How can I assist you today?
                        <div class="bilingual">
                            مرحباً بك في تأمين متكاملة!<br>
                            كيف يمكنني مساعدتك اليوم؟
                        </div>
                    </div>
                    <span class="message-time">Just now</span>
                </div>
            </div>
        </div>

        <div class="category-section">
            <div class="category-label" data-i18n="quickOptions">Quick Options</div>
            <div class="category-buttons">
                <button class="category-btn motor" onclick="handleCategoryClick(event, 'I need motor insurance')">
                    <div class="icon-bg"><i class="fas fa-car icon"></i></div>
                    <span class="label" data-i18n="motor">Motor</span>
                </button>
                <button class="category-btn health" onclick="handleCategoryClick(event, 'I need health insurance')">
                    <div class="icon-bg"><i class="fas fa-heartbeat icon"></i></div>
                    <span class="label" data-i18n="health">Health</span>
                </button>
                <button class="category-btn travel" onclick="handleCategoryClick(event, 'I need travel insurance')">
                    <div class="icon-bg"><i class="fas fa-plane icon"></i></div>
                    <span class="label" data-i18n="travel">Travel</span>
                </button>
                <button class="category-btn property" onclick="handleCategoryClick(event, 'I need property insurance')">
                    <div class="icon-bg"><i class="fas fa-home icon"></i></div>
                    <span class="label" data-i18n="property">Property</span>
                </button>
                <button class="category-btn claims" onclick="handleCategoryClick(event, 'I want to file a claim')">
                    <div class="icon-bg"><i class="fas fa-file-alt icon"></i></div>
                    <span class="label" data-i18n="claims">Claims</span>
                </button>
            </div>
        </div>

        <div class="chat-input">
            <input type="text" id="messageInput" placeholder="Type your message..." data-i18n-placeholder="messagePlaceholder" onkeypress="handleKeyPress(event)">
            <button class="send-btn" onclick="sendMessage()" title="Send message" data-i18n-title="sendMessage" aria-label="Send message">
                <i class="fas fa-paper-plane"></i>
            </button>
        </div>

        <div class="chat-footer">
            <svg width="16" height="16" viewBox="0 0 55 55" style="vertical-align: middle; margin-right: 5px;">
                <path d="M0.772116 16.3861C5.942 23.861 21.0231 53.3243 31.7953 53.3243C39.9883 53.3243 53.3474 18.5753 53.3474 10.2317C53.3358 -9.5637 -7.42866 3.18919 0.772116 16.3861Z" fill="#0C72E8"/>
                <path d="M25.413 5.40541L24.6408 5.01931C14.8378 1.18147 11.8185 35.6023 19.5752 34.834C24.6563 34.3282 41.6563 32.3475 43.6795 26.7259C44.6177 22.3668 29.7528 7.53668 25.413 5.40541Z" fill="#2E289E"/>
            </svg>
            Powered by Mutakamela AI • <a href="https://mutakamela.sa" target="_blank">mutakamela.sa</a>
            <button class="theme-toggle" onclick="toggleDarkMode()" title="Toggle Dark Mode" data-i18n-title="toggleTheme">
                <i class="fas fa-moon"></i>
            </button>
        </div>
    </div>

    <script>
        // Session persistence - restore or create new
        function createSessionId() {
            if (window.crypto && typeof window.crypto.randomUUID === 'function') {
                return window.crypto.randomUUID();
            }
            const bytes = new Uint8Array(16);
            window.crypto.getRandomValues(bytes);
            return Array.from(bytes, value => value.toString(16).padStart(2, '0')).join('');
        }

        function isValidSessionId(value) {
            return typeof value === 'string' && /^(?:[0-9a-f]{32}|[0-9a-f]{8}-(?:[0-9a-f]{4}-){3}[0-9a-f]{12})$/i.test(value);
        }

        const storedSessionId = localStorage.getItem('mutakamela_session');
        const legacySessionId = storedSessionId && !isValidSessionId(storedSessionId) ? storedSessionId : null;
        let sessionId = isValidSessionId(storedSessionId) ? storedSessionId : createSessionId();
        localStorage.setItem('mutakamela_session', sessionId);

        let currentLang = localStorage.getItem('mutakamela_lang') || 'en';
        let darkMode = localStorage.getItem('mutakamela_dark') === 'true';
        const apiBaseUrl = {{ api_base_url | tojson }};
        let conversationHistory = [];
        let displayHistory = [];
        let messageInFlight = false;
        let audioContext = null;
        const interfaceText = {
            en: {
                quickOptions: 'Quick Options', motor: 'Motor', health: 'Health', travel: 'Travel',
                property: 'Property', claims: 'Claims', messagePlaceholder: 'Type your message...',
                newChat: 'New Chat', sendMessage: 'Send message', toggleTheme: 'Toggle Dark Mode'
            },
            ar: {
                quickOptions: 'خيارات سريعة', motor: 'المركبات', health: 'الصحي', travel: 'السفر',
                property: 'الممتلكات', claims: 'المطالبات', messagePlaceholder: 'اكتب رسالتك...',
                newChat: 'محادثة جديدة', sendMessage: 'إرسال الرسالة', toggleTheme: 'تبديل المظهر الداكن'
            }
        };

        function normalizeStoredMessages(messages, allowedRoles, maximum) {
            if (!Array.isArray(messages)) return [];
            return messages
                .filter(item => item && allowedRoles.includes(item.role) && typeof item.content === 'string')
                .map(item => {
                    const content = item.content.slice(0, 4000).split(String.fromCharCode(10))
                        .filter((line, index, lines) => index === 0 || line.trim() !== lines[index - 1].trim())
                        .join(String.fromCharCode(10));
                    const normalized = { role: item.role, content };
                    if (item.role === 'assistant') {
                        const storedStage = typeof item.stage === 'string' ? item.stage.toUpperCase() : '';
                        normalized.stage = validChatStages.has(storedStage) ? storedStage : inferStoredStage(normalized.content);
                        normalized.isClaims = item.isClaims === true || /claim.*hotline|hotline.*claim/i.test(normalized.content);
                        normalized.isComplete = item.isComplete === true || normalized.stage === 'COMPLETE';
                        if (typeof item.retryMessage === 'string') normalized.retryMessage = item.retryMessage.slice(0, 4000);
                    }
                    return normalized;
                })
                .filter(item => item.content.trim())
                .slice(-maximum);
        }

        const validChatStages = new Set([
            'IDENTIFY', 'RECOMMEND', 'DETAILS', 'CONFIRM', 'COMPLETE',
            'CLAIM_EDIT', 'CLAIM_EDIT_VALUE', 'CLAIM_INTAKE', 'CLAIM_CONFIRM', 'CLAIM_COMPLETE'
        ]);

        function inferStoredStage(content) {
            const text = content.toLowerCase();
            if (text.includes('claim draft summary') || text.includes('ملخص مسودة المطالبة')) return 'CLAIM_CONFIRM';
            if (text.includes('claim draft is ready') || text.includes('مسودة المطالبة جاهزة')) return 'CLAIM_COMPLETE';
            if (text.includes('which detail would you like to edit') || text.includes('ما المعلومة التي ترغب في تعديلها')) return 'CLAIM_EDIT';
            if (text.includes('this chat can explain insurance products') || text.includes('يمكن لهذه المحادثة شرح منتجات التأمين')) return 'CONFIRM';
            if (text.includes('ready to proceed') || text.includes('مستعد للمتابعة')) return 'DETAILS';
            if (text.includes('would you like more details')) return 'RECOMMEND';
            if (text.includes('thank you for choosing')) return 'COMPLETE';
            return '';
        }

        function migrateLegacyChat(markup) {
            const parsed = new DOMParser().parseFromString(markup, 'text/html');
            const messages = Array.from(parsed.querySelectorAll('.message')).flatMap(message => {
                const bubble = message.querySelector('.message-bubble');
                if (!bubble) return [];
                const safeBubble = bubble.cloneNode(true);
                safeBubble.querySelectorAll('.quote-form, .quick-replies, .product-card, button').forEach(element => element.remove());
                const content = safeBubble.textContent.replace(/\\s+/g, ' ').trim();
                if (!content || content.startsWith('Welcome to Mutakamela Insurance!')) return [];
                return [{ role: message.classList.contains('user') ? 'user' : 'assistant', content }];
            });
            return {
                conversation: normalizeStoredMessages(messages, ['user', 'assistant'], 20),
                messages: normalizeStoredMessages(messages, ['user', 'assistant'], 50)
            };
        }

        function readSavedChat(storageKey) {
            const saved = localStorage.getItem(storageKey);
            if (!saved) return null;
            try {
                const parsed = JSON.parse(saved);
                return {
                    conversation: normalizeStoredMessages(parsed.conversation, ['user', 'assistant'], 20),
                    messages: normalizeStoredMessages(parsed.messages, ['user', 'assistant'], 50)
                };
            } catch {
                return migrateLegacyChat(saved);
            }
        }

        function saveChatHistory() {
            try {
                localStorage.setItem('mutakamela_chat_' + sessionId, JSON.stringify({
                    conversation: conversationHistory.slice(-20),
                    messages: displayHistory.slice(-50)
                }));
            } catch (error) {}
        }

        function loadChatHistory() {
            const storageKey = 'mutakamela_chat_' + sessionId;
            const saved = readSavedChat(storageKey) || (legacySessionId
                ? readSavedChat('mutakamela_chat_' + legacySessionId)
                : null);
            if (!saved) return;

            conversationHistory = saved.conversation;
            displayHistory = saved.messages;
            displayHistory.forEach(message => addMessage(message.content, message.role === 'user', {
                persist: false,
                plainText: true,
                stage: message.stage,
                isClaims: message.isClaims,
                isComplete: message.isComplete,
                retryMessage: message.retryMessage,
                restoreActions: true
            }));
            saveChatHistory();
            if (legacySessionId) localStorage.removeItem('mutakamela_chat_' + legacySessionId);
        }

        // Initialize on load
        document.addEventListener('DOMContentLoaded', () => {
            // Restore dark mode
            if (darkMode) {
                document.body.classList.add('dark-mode');
                document.querySelector('.theme-toggle i').className = 'fas fa-sun';
            }
            currentLang = currentLang === 'ar' ? 'ar' : 'en';
            document.querySelectorAll('[data-language]').forEach(button => {
                button.classList.toggle('active', button.dataset.language === currentLang);
            });
            applyLanguage();
            loadChatHistory();
        });

        function applyLanguage() {
            const labels = interfaceText[currentLang];
            document.querySelectorAll('[data-i18n]').forEach(element => {
                element.textContent = labels[element.dataset.i18n];
            });
            document.querySelectorAll('[data-i18n-title]').forEach(element => {
                element.title = labels[element.dataset.i18nTitle];
            });
            document.querySelectorAll('[data-i18n-placeholder]').forEach(element => {
                element.placeholder = labels[element.dataset.i18nPlaceholder];
            });
            document.documentElement.lang = currentLang;
            document.documentElement.dir = currentLang === 'ar' ? 'rtl' : 'ltr';
        }

        function getActionLabels(language) {
            return language === 'ar'
                ? {
                    moreDetails: 'تفاصيل أكثر', comparePlans: 'مقارنة الخطط الأخرى',
                    continuePlan: 'المتابعة بهذه الخطة', askQuestion: 'طرح سؤال',
                    contact: 'تواصل مع متكاملة', confirmDraft: 'تأكيد المسودة',
                    editDetails: 'تعديل التفاصيل', submit: 'التقديم عبر متكاملة',
                    newClaim: 'مسودة مطالبة جديدة', anotherProduct: 'منتج آخر',
                    startOver: 'بدء محادثة جديدة', retry: 'إعادة المحاولة'
                }
                : {
                    moreDetails: 'More details', comparePlans: 'Compare other plans',
                    continuePlan: 'Continue with this plan', askQuestion: 'Ask a question',
                    contact: 'Contact Mutakamela', confirmDraft: 'Confirm draft',
                    editDetails: 'Edit details', submit: 'Submit through Mutakamela',
                    newClaim: 'New claim draft', anotherProduct: 'Another product',
                    startOver: 'Start over', retry: 'Retry'
                };
        }

        function buildStageActions(stage, isClaims, isComplete, language) {
            const labels = getActionLabels(language);
            let actions = [];

            if (!isClaims && stage === 'RECOMMEND') {
                actions = [
                    { icon: 'fa-info-circle', label: labels.moreDetails, message: 'Tell me more about this plan' },
                    { icon: 'fa-th-list', label: labels.comparePlans, message: 'Show me other options' }
                ];
            } else if (!isClaims && stage === 'DETAILS') {
                actions = [
                    { icon: 'fa-check', label: labels.continuePlan, message: 'I am interested in this plan' },
                    { icon: 'fa-th-list', label: labels.comparePlans, message: 'Show me other options' },
                    { icon: 'fa-question-circle', label: labels.askQuestion, message: 'I have a question about this plan' }
                ];
            } else if (!isClaims && stage === 'CONFIRM') {
                actions = [
                    { icon: 'fa-external-link-alt', label: labels.contact, href: 'https://mutakamela.sa' },
                    { icon: 'fa-th-list', label: labels.comparePlans, message: 'Show me other options' },
                    { icon: 'fa-question-circle', label: labels.askQuestion, message: 'I have a question about insurance' }
                ];
            } else if (stage === 'CLAIM_EDIT') {
                const fields = language === 'ar'
                    ? [['رقم الوثيقة', 'رقم الوثيقة', 'fa-file-alt'], ['تاريخ الحادث', 'تاريخ الحادث', 'fa-calendar'], ['وصف الحادث', 'وصف الحادث', 'fa-pen'], ['معلومات التواصل', 'معلومات التواصل', 'fa-address-card']]
                    : [['Policy reference', 'Policy reference', 'fa-file-alt'], ['Incident date', 'Incident date', 'fa-calendar'], ['Incident details', 'Incident details', 'fa-pen'], ['Contact details', 'Contact details', 'fa-address-card']];
                actions = fields.map(([message, label, icon]) => ({ icon, label, message }));
            } else if (stage === 'CLAIM_CONFIRM') {
                actions = [
                    { icon: 'fa-check', label: labels.confirmDraft, message: 'Confirm claim draft' },
                    { icon: 'fa-edit', label: labels.editDetails, message: 'Edit claim details' }
                ];
            } else if (stage === 'CLAIM_COMPLETE') {
                actions = [
                    { icon: 'fa-external-link-alt', label: labels.submit, href: 'https://mutakamela.sa' },
                    { icon: 'fa-plus', label: labels.newClaim, message: 'Start a new claim' }
                ];
            } else if (isComplete) {
                actions = [
                    { icon: 'fa-plus', label: labels.anotherProduct, message: 'I need another insurance' },
                    { icon: 'fa-redo', label: labels.startOver, newChat: true }
                ];
            }

            if (!actions.length) return '';
            return `<div class="quick-replies">${actions.map(action => {
                const icon = `<i class="fas ${action.icon}"></i> `;
                if (action.href) return `<a class="quick-reply" href="${action.href}" target="_blank" rel="noopener noreferrer" style="text-decoration: none;">${icon}${action.label}</a>`;
                const handler = action.newChat ? 'startNewChat()' : `sendQuick('${action.message}')`;
                return `<button type="button" class="quick-reply" onclick="${handler}">${icon}${action.label}</button>`;
            }).join('')}</div>`;
        }

        function setLang(lang, button) {
            if (!['en', 'ar'].includes(lang)) return;
            currentLang = lang;
            localStorage.setItem('mutakamela_lang', lang);
            document.querySelectorAll('[data-language]').forEach(languageButton => {
                languageButton.classList.toggle('active', languageButton === button);
            });
            applyLanguage();
        }

        function toggleDarkMode() {
            darkMode = !darkMode;
            localStorage.setItem('mutakamela_dark', darkMode);
            document.body.classList.toggle('dark-mode');
            const icon = document.querySelector('.theme-toggle i');
            icon.className = darkMode ? 'fas fa-sun' : 'fas fa-moon';
        }

        function escapeHTML(value) {
            const escapes = { '&': '&amp;', '<': '&lt;', '>': '&gt;', '"': '&quot;', "'": '&#39;' };
            return String(value ?? '').replace(/[&<>"']/g, character => escapes[character]);
        }

        function prepareAudio() {
            const AudioContextType = window.AudioContext || window.webkitAudioContext;
            if (!AudioContextType) return;
            try {
                if (!audioContext) audioContext = new AudioContextType();
                if (audioContext.state === 'suspended') audioContext.resume().catch(() => {});
            } catch (error) {
                audioContext = null;
            }
        }

        function playNotificationSound() {
            if (!audioContext || audioContext.state !== 'running') return;
            const oscillator = audioContext.createOscillator();
            const gainNode = audioContext.createGain();
            oscillator.connect(gainNode);
            gainNode.connect(audioContext.destination);
            oscillator.frequency.value = 800;
            oscillator.type = 'sine';
            gainNode.gain.setValueAtTime(0.1, audioContext.currentTime);
            gainNode.gain.exponentialRampToValueAtTime(0.01, audioContext.currentTime + 0.3);
            oscillator.start(audioContext.currentTime);
            oscillator.stop(audioContext.currentTime + 0.3);
        }

        function addMessage(content, isUser = false, options = {}) {
            const messagesDiv = document.getElementById('chatMessages');
            const time = new Date().toLocaleTimeString([], {hour: '2-digit', minute:'2-digit'});

            const avatar = isUser
                ? '<i class="fas fa-user"></i>'
                : '<i class="fas fa-headset"></i>';

            const messageDiv = document.createElement('div');
            messageDiv.className = `message ${isUser ? 'user' : 'bot'}`;
            const avatarElement = document.createElement('div');
            avatarElement.className = 'message-avatar';
            avatarElement.innerHTML = avatar;
            const messageContent = document.createElement('div');
            messageContent.className = 'message-content';
            const bubble = document.createElement('div');
            bubble.className = 'message-bubble';
            if (isUser || options.plainText) bubble.textContent = String(content);
            else bubble.innerHTML = content;
            if (!isUser && options.restoreActions && options.stage) {
                bubble.insertAdjacentHTML('beforeend', buildStageActions(options.stage, options.isClaims, options.isComplete, currentLang));
            }
            if (!isUser && typeof options.retryMessage === 'string') {
                const retryButton = document.createElement('button');
                retryButton.type = 'button';
                retryButton.className = 'quick-reply retry-btn';
                retryButton.dataset.message = options.retryMessage;
                retryButton.innerHTML = `<i class="fas fa-redo"></i> ${getActionLabels(currentLang).retry}`;
                retryButton.addEventListener('click', () => retryLastMessage(retryButton));
                bubble.append(' ', retryButton);
            }
            const timeElement = document.createElement('span');
            timeElement.className = 'message-time';
            timeElement.textContent = time;
            messageContent.append(bubble, timeElement);
            messageDiv.append(avatarElement, messageContent);
            messagesDiv.appendChild(messageDiv);
            messagesDiv.scrollTop = messagesDiv.scrollHeight;
            if (options.persist !== false) {
                const savedMessage = {
                    role: isUser ? 'user' : 'assistant',
                    content: String(options.historyText ?? content)
                };
                if (!isUser && options.stage) {
                    savedMessage.stage = options.stage;
                    savedMessage.isClaims = options.isClaims === true;
                    savedMessage.isComplete = options.isComplete === true;
                }
                if (!isUser && typeof options.retryMessage === 'string') savedMessage.retryMessage = options.retryMessage;
                displayHistory.push(savedMessage);
                displayHistory = displayHistory.slice(-50);
                saveChatHistory();
            }
        }

        function showTyping() {
            const messagesDiv = document.getElementById('chatMessages');
            const typingDiv = document.createElement('div');
            typingDiv.id = 'typingIndicator';
            typingDiv.className = 'typing-indicator';
            typingDiv.innerHTML = '<span></span><span></span><span></span>';
            messagesDiv.appendChild(typingDiv);
            messagesDiv.scrollTop = messagesDiv.scrollHeight;
        }

        function hideTyping() {
            const typing = document.getElementById('typingIndicator');
            if (typing) typing.remove();
        }

        function setChatControlsDisabled(disabled) {
            document.getElementById('messageInput').disabled = disabled;
            document.querySelector('.send-btn').disabled = disabled;
            document.querySelectorAll('.category-btn, .quick-reply, .new-chat-action').forEach(button => {
                if (button instanceof HTMLButtonElement) button.disabled = disabled;
            });
        }

        async function sendMessage(messageOverride = null, isRetry = false, retryButton = null) {
            const input = document.getElementById('messageInput');
            if (messageInFlight) return false;
            const message = isRetry ? String(messageOverride || '').trim() : String(messageOverride ?? input.value).trim();
            if (!message) return false;

            const actionLabels = getActionLabels(currentLang);

            prepareAudio();
            messageInFlight = true;
            if (!isRetry) {
                addMessage(message, true);
                conversationHistory.push({ role: 'user', content: message });
                conversationHistory = conversationHistory.slice(-20);
                saveChatHistory();
            }
            input.value = '';
            setChatControlsDisabled(true);
            showTyping();

            try {
                const response = await fetch(`${apiBaseUrl}/api/chat`, {
                    method: 'POST',
                    headers: { 'Content-Type': 'application/json' },
                    body: JSON.stringify({
                        sessionId: sessionId,
                        message: message,
                        language: currentLang
                    })
                });

                if (!response.ok) throw new Error(`Chat request failed (${response.status})`);
                const data = await response.json();
                if (!data || typeof data !== 'object') throw new Error('Invalid chat response');
                hideTyping();

                const rawResponse = typeof data.response === 'string' && data.response
                    ? data.response
                    : currentLang === 'ar' ? 'عذراً، حدث خطأ.' : 'Sorry, I encountered an error.';
                let botResponse = escapeHTML(rawResponse);

                const responseArabic = data.responseAr ?? data.response_ar;
                if (typeof responseArabic === 'string' && responseArabic.trim() && responseArabic.trim() !== rawResponse.trim()) {
                    botResponse += `<div class="bilingual">${escapeHTML(responseArabic)}</div>`;
                }

                const selectedProduct = data.selectedProduct ?? data.selected_product;
                const productDetails = data.productDetails ?? data.product_details;
                if (selectedProduct && typeof selectedProduct.name === 'string') {
                    const localizedName = currentLang === 'ar' && typeof selectedProduct.nameAr === 'string' && selectedProduct.nameAr
                        ? selectedProduct.nameAr
                        : selectedProduct.name;
                    const coverageSource = currentLang === 'ar' ? productDetails?.coverageAr : productDetails?.coverage;
                    const coverage = Array.isArray(coverageSource)
                        ? coverageSource.filter(item => typeof item === 'string').slice(0, 3)
                        : [];
                    botResponse += `
                        <div class="product-card">
                            <h4><i class="fas fa-shield-alt"></i> ${escapeHTML(localizedName)}</h4>
                            ${coverage.length ?
                                '<ul>' + coverage.map(item => `<li>${escapeHTML(item)}</li>`).join('') + '</ul>'
                                : ''}
                        </div>
                    `;
                }

                // Match actions to the bot's current conversation stage.
                const responseText = (data.response || '').toLowerCase();
                const stage = typeof data.stage === 'string' ? data.stage.toUpperCase() : '';

                const isComplete = stage === 'COMPLETE' ||
                                   responseText.includes('thank you for choosing');
                const isClaims = responseText.includes('claim') && responseText.includes('hotline');

                // Show appropriate quick replies
                if (!isClaims && stage === 'RECOMMEND') {
                    botResponse += `
                        <div class="quick-replies">
                            <button class="quick-reply" onclick="sendQuick('Tell me more about this plan')">
                                <i class="fas fa-info-circle"></i> ${actionLabels.moreDetails}
                            </button>
                            <button class="quick-reply" onclick="sendQuick('Show me other options')">
                                <i class="fas fa-th-list"></i> ${actionLabels.comparePlans}
                            </button>
                        </div>
                    `;
                } else if (!isClaims && stage === 'DETAILS') {
                    botResponse += `
                        <div class="quick-replies">
                            <button class="quick-reply" onclick="sendQuick('I am interested in this plan')">
                                <i class="fas fa-check"></i> ${actionLabels.continuePlan}
                            </button>
                            <button class="quick-reply" onclick="sendQuick('Show me other options')">
                                <i class="fas fa-th-list"></i> ${actionLabels.comparePlans}
                            </button>
                            <button class="quick-reply" onclick="sendQuick('I have a question about this plan')">
                                <i class="fas fa-question-circle"></i> ${actionLabels.askQuestion}
                            </button>
                        </div>
                    `;
                } else if (!isClaims && stage === 'CONFIRM') {
                    botResponse += `
                        <div class="quick-replies">
                            <a class="quick-reply" href="https://mutakamela.sa" target="_blank" rel="noopener noreferrer" style="text-decoration: none;">
                                <i class="fas fa-external-link-alt"></i> ${actionLabels.contact}
                            </a>
                            <button class="quick-reply" onclick="sendQuick('Show me other options')">
                                <i class="fas fa-th-list"></i> ${actionLabels.comparePlans}
                            </button>
                            <button class="quick-reply" onclick="sendQuick('I have a question about insurance')">
                                <i class="fas fa-question-circle"></i> ${actionLabels.askQuestion}
                            </button>
                        </div>
                    `;
                } else if (stage === 'CLAIM_EDIT') {
                    const editChoices = currentLang === 'ar'
                        ? [
                            ['رقم الوثيقة', 'رقم الوثيقة', 'fa-file-alt'],
                            ['تاريخ الحادث', 'تاريخ الحادث', 'fa-calendar'],
                            ['وصف الحادث', 'وصف الحادث', 'fa-pen'],
                            ['معلومات التواصل', 'معلومات التواصل', 'fa-address-card']
                        ]
                        : [
                            ['Policy reference', 'Policy reference', 'fa-file-alt'],
                            ['Incident date', 'Incident date', 'fa-calendar'],
                            ['Incident details', 'Incident details', 'fa-pen'],
                            ['Contact details', 'Contact details', 'fa-address-card']
                        ];
                    botResponse += `
                        <div class="quick-replies">
                            ${editChoices.map(([message, label, icon]) => `
                                <button class="quick-reply" onclick="sendQuick('${message}')">
                                    <i class="fas ${icon}"></i> ${label}
                                </button>
                            `).join('')}
                        </div>
                    `;
                } else if (stage === 'CLAIM_CONFIRM') {
                    botResponse += `
                        <div class="quick-replies">
                            <button class="quick-reply" onclick="sendQuick('Confirm claim draft')">
                                <i class="fas fa-check"></i> ${actionLabels.confirmDraft}
                            </button>
                            <button class="quick-reply" onclick="sendQuick('Edit claim details')">
                                <i class="fas fa-edit"></i> ${actionLabels.editDetails}
                            </button>
                        </div>
                    `;
                } else if (stage === 'CLAIM_COMPLETE') {
                    botResponse += `
                        <div class="quick-replies">
                            <a class="quick-reply" href="https://mutakamela.sa" target="_blank" rel="noopener noreferrer" style="text-decoration: none;">
                                <i class="fas fa-external-link-alt"></i> ${actionLabels.submit}
                            </a>
                            <button class="quick-reply" onclick="sendQuick('Start a new claim')">
                                <i class="fas fa-plus"></i> ${actionLabels.newClaim}
                            </button>
                        </div>
                    `;
                } else if (isComplete) {
                    botResponse += `
                        <div class="quick-replies">
                            <button class="quick-reply" onclick="sendQuick('I need another insurance')">
                                <i class="fas fa-plus"></i> ${actionLabels.anotherProduct}
                            </button>
                            <button class="quick-reply" onclick="startNewChat()">
                                <i class="fas fa-redo"></i> ${actionLabels.startOver}
                            </button>
                        </div>
                    `;
                }

                const conversationResponse = typeof data.conversation_response === 'string'
                    ? data.conversation_response
                    : rawResponse;
                conversationHistory.push({ role: 'assistant', content: conversationResponse });
                conversationHistory = conversationHistory.slice(-20);
                addMessage(botResponse, false, {
                    historyText: [
                        rawResponse,
                        typeof responseArabic === 'string' && responseArabic.trim() !== rawResponse.trim() ? responseArabic : null
                    ].filter(Boolean).join(String.fromCharCode(10)),
                    stage,
                    isClaims,
                    isComplete
                });
                saveChatHistory();
                if (retryButton) retryButton.remove();
                playNotificationSound();
                return true;

            } catch (error) {
                hideTyping();
                if (retryButton) retryButton.remove();
                const errorMessage = currentLang === 'ar'
                    ? 'عذراً، حدث خطأ. يرجى المحاولة مرة أخرى.'
                    : 'Sorry, there was an error. Please try again.';
                addMessage(errorMessage, false, {
                    historyText: errorMessage,
                    retryMessage: message
                });
                const retryControl = document.querySelector('#chatMessages .message:last-child .retry-btn');
                retryControl.dataset.message = message;
                return false;
            } finally {
                messageInFlight = false;
                setChatControlsDisabled(false);
            }
        }

        function retryLastMessage(button) {
            button.disabled = true;
            return sendMessage(button.dataset.message, true, button);
        }

        function sendQuick(message) {
            if (messageInFlight) return Promise.resolve(false);
            document.getElementById('messageInput').value = message;
            return sendMessage();
        }

        function handleCategoryClick(event, message) {
            // Create ripple effect
            const btn = event.currentTarget;
            const ripple = document.createElement('span');
            ripple.className = 'ripple';
            const rect = btn.getBoundingClientRect();
            const size = Math.max(rect.width, rect.height);
            ripple.style.width = ripple.style.height = size + 'px';
            ripple.style.left = (event.clientX - rect.left - size/2) + 'px';
            ripple.style.top = (event.clientY - rect.top - size/2) + 'px';
            btn.appendChild(ripple);

            setTimeout(() => ripple.remove(), 600);

            // Send message
            sendQuick(message);
        }

        function handleKeyPress(event) {
            if (event.key === 'Enter') sendMessage();
        }

        async function startNewChat() {
            if (messageInFlight) return;
            messageInFlight = true;
            setChatControlsDisabled(true);
            try {
                try {
                    await fetch(`${apiBaseUrl}/api/chat/${encodeURIComponent(sessionId)}`, { method: 'DELETE' });
                } catch (e) {}

                localStorage.removeItem('mutakamela_chat_' + sessionId);
                conversationHistory = [];
                displayHistory = [];
                sessionId = createSessionId();
                localStorage.setItem('mutakamela_session', sessionId);

                const messagesDiv = document.getElementById('chatMessages');
                messagesDiv.innerHTML = `
                <div class="message bot">
                    <div class="message-avatar"><i class="fas fa-headset"></i></div>
                    <div class="message-content">
                        <div class="message-bubble">
                            <strong>Welcome to Mutakamela Insurance!</strong> 👋<br><br>
                            I'm here to help you find the right coverage. How can I assist you today?
                            <div class="bilingual">
                                مرحباً بك في تأمين متكاملة!<br>
                                كيف يمكنني مساعدتك اليوم؟
                            </div>
                        </div>
                        <span class="message-time">Just now</span>
                    </div>
                </div>
            `;
            } finally {
                messageInFlight = false;
                setChatControlsDisabled(false);
            }
        }
    </script>
</body>
</html>
'''

@app.route('/')
def home():
    api_base_url = os.getenv('DOTNET_API_BASE_URL', 'http://localhost:5000').rstrip('/')
    return render_template_string(CHAT_HTML, api_base_url=api_base_url)

@app.route('/api/chat', methods=['POST'])
@limiter.limit(os.getenv('CHAT_RATE_LIMIT', '20 per minute'))
def chat():
    data = request.get_json(silent=True)
    if not isinstance(data, dict):
        return jsonify({'error': 'Request body must be a JSON object'}), 400

    session_id = data.get('session_id')
    if not isinstance(session_id, str) or not SESSION_ID_PATTERN.fullmatch(session_id):
        return jsonify({'error': 'A valid session_id is required'}), 400

    message = data.get('message')
    if not isinstance(message, str) or not message.strip():
        return jsonify({'error': 'A non-empty message is required'}), 400
    message = message.strip()
    if len(message) > MAX_MESSAGE_LENGTH:
        return jsonify({'error': f'Message must be {MAX_MESSAGE_LENGTH} characters or fewer'}), 413

    lang = data.get('lang', 'en')
    if lang not in ('en', 'ar'):
        return jsonify({'error': 'lang must be "en" or "ar"'}), 400

    history = None
    if 'history' in data:
        try:
            history = normalize_conversation_history(data['history'])
        except ValueError as error:
            return jsonify({'error': str(error)}), 400

    now = time.time()
    cleanup_old_sessions(now)

    session = sessions.get(session_id)
    if session is None:
        session = {'ai': PolicySelectorAdesso(), 'created': now}
        sessions[session_id] = session
    session['last_accessed'] = now
    ai = session['ai']
    if history is not None:
        ai.conversation_history = history

    try:
        response = ai.select_policy(message, lang=lang)

        if response.pop('_error', False):
            return jsonify({
                'response': response.get('response', 'Sorry, I encountered an error. Please try again.'),
                'response_ar': response.get('response_ar', 'عذراً، حدث خطأ. يرجى المحاولة مرة أخرى.'),
                'session_id': session_id
            }), 502

        response['conversation_response'] = response.get('response', '')
        # If Arabic mode, swap responses
        if lang == 'ar' and response.get('response_ar'):
            response['response'], response['response_ar'] = response['response_ar'], response['response']

        response['session_id'] = session_id
        return jsonify(response)
    except Exception as e:
        app.logger.exception('Chat request failed')
        return jsonify({
            'response': 'Sorry, I encountered an error. Please try again.',
            'response_ar': 'عذراً، حدث خطأ. يرجى المحاولة مرة أخرى.'
        }), 502

@app.route('/api/health')
def health():
    return jsonify({'status': 'healthy', 'service': 'Mutakamela AI Chat'})

@app.route('/api/clear/<session_id>', methods=['DELETE'])
def clear_session(session_id):
    if not SESSION_ID_PATTERN.fullmatch(session_id):
        return jsonify({'error': 'A valid session_id is required'}), 400
    if session_id in sessions:
        sessions[session_id]['ai'].clear_history()
        del sessions[session_id]
    return jsonify({'status': 'cleared'})

if __name__ == '__main__':
    port = int(os.getenv('PORT', 5000))
    print(f"""
    ╔═══════════════════════════════════════════════════════╗
    ║                                                       ║
    ║   🏢 MUTAKAMELA INSURANCE AI - Web Chat               ║
    ║                                                       ║
    ║   Open in browser: http://localhost:{port}             ║
    ║                                                       ║
    ╚═══════════════════════════════════════════════════════╝
    """)
    host = os.getenv('HOST', '127.0.0.1')
    debug = os.getenv('FLASK_DEBUG', '').lower() in ('1', 'true', 'yes')
    app.run(host=host, port=port, debug=debug)
