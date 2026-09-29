"""
Mutakamela AI Policy Selector - Interactive Conversation
=========================================================
Natural conversation flow with no repetition!

Setup:
    1. Update .env with your API key
    2. pip install openai python-dotenv
    3. python policy_selector_adesso.py
"""

import json
import os
from pathlib import Path
from typing import List, Dict
from dotenv import load_dotenv

load_dotenv(override=True)

try:
    from openai import OpenAI
except ImportError:
    print("❌ Please install: pip install openai python-dotenv")
    exit(1)


class PolicySelectorAdesso:
    """Interactive AI Policy Selector with conversation memory"""

    def __init__(self):
        self.products = self._load_products()
        self.conversation_history = []
        self.current_step = "START"  # Track conversation stage

        api_key = os.getenv("ADESSO_API_KEY")
        api_base = os.getenv("ADESSO_API_BASE", "https://adesso-ai-hub.3asabc.de/v1")
        self.model = os.getenv("MODEL_NAME", "gpt-4.1-mini")

        if not api_key:
            print("⚠️  Please set ADESSO_API_KEY in .env file!")
            exit(1)

        self.client = OpenAI(api_key=api_key, base_url=api_base)
        print(f"✅ Connected to adesso AI Hub")
        print(f"   Model: {self.model}")

    def _load_products(self) -> List[Dict]:
        data_path = Path(__file__).parent / "Data" / "products.json"
        with open(data_path, 'r', encoding='utf-8') as f:
            return json.load(f)['products']

    def _get_product_details(self, product_id: str) -> Dict:
        """Get full details of a specific product"""
        for p in self.products:
            if p['id'] == product_id:
                return p
        return None

    def clear_history(self):
        self.conversation_history = []
        self.current_step = "START"
        print("🗑️  Conversation cleared - Starting fresh!")

    def select_policy(self, user_input: str, lang: str = 'en') -> Dict:
        """Process user input with natural conversation flow"""

        self.conversation_history.append({"role": "user", "content": user_input})

        # Build conversation context
        conv_text = "\n".join([
            f"{'Customer' if m['role']=='user' else 'AI'}: {m['content']}"
            for m in self.conversation_history[-6:]  # Last 6 messages
        ])

        # Get relevant product details
        product_details = self._build_product_details()

        system_prompt = """You are Mutakamela Insurance AI - friendly, helpful, and NEVER repetitive.

CRITICAL RULES:
1. NEVER repeat information you already gave
2. Keep responses SHORT (2-3 sentences max)
3. Be conversational, not robotic
4. ALWAYS move the conversation forward

UNDERSTAND USER INTENT:
- "Tell me more" / "More info" / "Details" → GIVE MORE DETAILS about the product (coverage, benefits, price in SAR)
- "Yes" / "Proceed" / "Go ahead" / "I want this" → ASK for their name and phone to prepare quote
- "Other options" / "Something else" → SHOW alternative products
- "File a claim" / "Claim" / "Had an accident" → Start CLAIMS flow

CONVERSATION FLOW:
1. IDENTIFY → Customer tells need → Recommend a product briefly + ask "Would you like more details or ready to proceed?"
2. RECOMMEND → If they want more info → Give detailed coverage, benefits, price (SAR)
3. DETAILS → If they want to proceed → Ask: "Great! May I have your name and phone number?"
4. CONFIRM → Got name/phone → "Perfect! I'll send the quote to [phone]. Anything else?"
5. COMPLETE → Done → "Thank you for choosing Mutakamela! 🎉"

CLAIMS FLOW:
1. Ask: "I'm sorry to hear that. What type of claim? (Motor accident, Medical, Travel, etc.)"
2. Collect: Policy number or details
3. Guide them to:
   - File online: eservices.mutakamela.sa/myInsurance/make-a-claim
   - Call: 800-301-0001 (toll-free)
   - Track status: eservices.mutakamela.sa/myInsurance/claim-center

IMPORTANT COMPANY INFO:
- Toll-free: 800-301-0001
- Email: Customerservice@mutakamela.sa
- eServices Portal: eservices.mutakamela.sa
- Medical Network TPA: Nextcare & GlobeMed
- Mobile App: Available on App Store & Google Play
- Location: Khorais Road, Malaz, Riyadh

PRODUCT INFO TO SHARE ON "Tell me more":
- Coverage list with brief explanations
- Key benefits (what sets it apart)
- Starting price in SAR (Motor: from SAR 500/year, Health: from SAR 2,000/year, Travel: from SAR 50/trip, Property: from SAR 800/year)
- Why customers choose this

PRICING GUIDE (approximate starting prices):
- Motor Insurance: from SAR 500/year (Third Party), SAR 1,500/year (Comprehensive)
- Health Insurance: from SAR 2,000/year (Individual), SAR 5,000/year (Family)
- Travel Insurance: from SAR 50/trip (Basic), SAR 150/trip (Premium)
- Property Insurance: from SAR 800/year (Home), SAR 2,500/year (Commercial)
- Life Protection: from SAR 1,200/year

Always respond in JSON format."""

        user_prompt = f"""PRODUCT CATALOG:
{product_details}

CONVERSATION SO FAR:
{conv_text}

CURRENT MESSAGE: "{user_input}"

IMPORTANT - Understand the customer's intent:
- "Tell me more" / "More info" → Give DETAILED product info (coverage, benefits, price in SAR)
- "Yes" / "Proceed" → Ask for name and phone number
- "Other options" → Show different products

Respond appropriately based on what they asked:

JSON Response:
{{
    "stage": "IDENTIFY|RECOMMEND|DETAILS|CONFIRM|COMPLETE",
    "intent": "BUY_POLICY|GET_QUOTE|GET_INFO|CLAIM",
    "detected_lob": "MOTOR|HEALTH|TRAVEL|PROPERTY|MARINE|LIABILITY|ENGINEERING|CREDIT|PECUNIARY|SAVINGS|PROTECTION",
    "confidence": 0.95,
    "selected_product": {{"id": "", "name": "", "name_ar": "", "category": "INDIVIDUAL|CORPORATE"}},
    "product_details": {{
        "coverage": ["list of coverage items from product"],
        "features": ["key product features"],
        "key_benefits": ["benefit1", "benefit2"]
    }},
    "response": "Your natural, non-repetitive response to customer",
    "response_ar": "الرد بالعربية",
    "next_action": "What happens next"
}}"""

        try:
            response = self.client.chat.completions.create(
                model=self.model,
                messages=[
                    {"role": "system", "content": system_prompt},
                    {"role": "user", "content": user_prompt}
                ],
                temperature=0.4,
                max_tokens=1200,
                timeout=30
            )

            response_text = response.choices[0].message.content.strip()

            # Clean JSON
            if "```" in response_text:
                response_text = response_text.split("```")[1]
                if response_text.startswith("json"):
                    response_text = response_text[4:]
                response_text = response_text.strip()

            result = json.loads(response_text)
            result['model'] = self.model

            # Store AI response
            self.conversation_history.append({
                "role": "assistant",
                "content": result.get('response', '')
            })

            return result

        except Exception as e:
            print(f"❌ Error: {e}")
            return self._fallback()

    def _build_product_details(self) -> str:
        """Build detailed product catalog"""
        details = []
        current_lob = ""
        for p in self.products:
            if p['lob'] != current_lob:
                current_lob = p['lob']
                details.append(f"\n=== {current_lob} ===")

            coverage = ", ".join(p.get('coverage', [])[:4])  # First 4 coverages
            features = ", ".join(p.get('features', [])[:3])  # First 3 features
            details.append(f"""
{p['id']}: {p['name']} ({p['name_ar']})
  Category: {p.get('category', 'N/A')}
  Description: {p.get('description', '')[:150]}...
  Coverage: {coverage}
  Features: {features}
  Best for: {', '.join(p.get('suitable_for', []))}""")
        return '\n'.join(details)

    def _fallback(self) -> Dict:
        return {
            'stage': 'IDENTIFY',
            '_error': True,
            'response': "I'm here to help! What type of insurance are you looking for?",
            'response_ar': "أنا هنا للمساعدة! ما نوع التأمين الذي تبحث عنه؟"
        }


def print_response(result: Dict):
    """Clean, interactive output"""
    print("\n" + "─"*60)

    stage = result.get('stage', '')
    if stage:
        stage_emoji = {"IDENTIFY": "🔍", "RECOMMEND": "📋", "DETAILS": "📄", "CONFIRM": "✅", "COMPLETE": "🎉"}
        print(f"{stage_emoji.get(stage, '💬')} Stage: {stage}")

    if result.get('detected_lob') and result.get('detected_lob') != 'UNKNOWN':
        print(f"📁 Category: {result.get('detected_lob')}")

    # Main response
    print(f"\n🤖 Mutakamela: {result.get('response', 'How can I help?')}")

    # Show selected product info
    product = result.get('selected_product', {})
    if product.get('name'):
        print(f"\n   🏷️  Product: {product['name']}")
        if product.get('category'):
            print(f"   📂 Type: {product['category']}")

    # Show product details if available
    details = result.get('product_details', {})
    if details.get('coverage'):
        print(f"   📦 Coverage: {', '.join(details['coverage'][:5])}")
    if details.get('features'):
        print(f"   ✨ Features: {', '.join(details['features'][:3])}")
    if details.get('key_benefits'):
        print(f"   ⭐ Benefits: {', '.join(details['key_benefits'][:3])}")

    # Arabic response
    if result.get('response_ar'):
        print(f"\n   🇸🇦 {result.get('response_ar')}")

    # Next action hint
    if result.get('next_action'):
        print(f"\n   ➡️  Next: {result.get('next_action')}")

    print("─"*60)


def main():
    print("\n" + "="*60)
    print("🏢 MUTAKAMELA AI - Interactive Insurance Assistant")
    print("="*60)
    print("Chat naturally! Examples:")
    print("  • my car had an accident")
    print("  • I need health insurance for my family")
    print("  • travel insurance to Europe")
    print("\nCommands: 'clear' = restart | 'quit' = exit\n")

    selector = PolicySelectorAdesso()

    while True:
        try:
            user_input = input("👤 You: ").strip()

            if not user_input:
                continue
            if user_input.lower() in ['quit', 'exit', 'q']:
                print("👋 Thank you for using Mutakamela!")
                break
            if user_input.lower() == 'clear':
                selector.clear_history()
                continue

            result = selector.select_policy(user_input)
            print_response(result)

        except KeyboardInterrupt:
            print("\n👋 Goodbye!")
            break


if __name__ == "__main__":
    main()
