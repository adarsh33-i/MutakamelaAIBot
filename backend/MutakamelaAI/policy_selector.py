"""
Mutakamela AI Policy Selector
=============================
Step 1: User types something → AI decides which policy (out of 44)

Usage:
    python policy_selector.py

Dependencies:
    pip install openai python-dotenv
"""

import json
import os
from pathlib import Path
from typing import List, Dict, Optional
import re

# Try to import OpenAI, fall back to rule-based if not available
try:
    from openai import OpenAI
    OPENAI_AVAILABLE = True
except ImportError:
    OPENAI_AVAILABLE = False
    print("⚠️  OpenAI not installed. Using rule-based matching.")
    print("   To use AI: pip install openai")


class PolicySelector:
    """AI-powered policy selector for Mutakamela Insurance"""

    def __init__(self, use_ai: bool = True):
        self.products = self._load_products()
        self.use_ai = use_ai and OPENAI_AVAILABLE

        if self.use_ai:
            api_key = os.getenv("OPENAI_API_KEY")
            if api_key:
                self.client = OpenAI(api_key=api_key)
            else:
                print("⚠️  OPENAI_API_KEY not set. Using rule-based matching.")
                self.use_ai = False

    def _load_products(self) -> List[Dict]:
        """Load products from JSON file"""
        data_path = Path(__file__).parent / "Data" / "products.json"
        with open(data_path, 'r', encoding='utf-8') as f:
            data = json.load(f)
        return data['products']

    def select_policy(self, user_input: str) -> Dict:
        """
        Main method: Takes user input, returns best matching policies

        Args:
            user_input: What the customer said/typed

        Returns:
            Dict with intent, matched policies, and suggested response
        """
        if self.use_ai:
            return self._ai_select(user_input)
        else:
            return self._rule_based_select(user_input)

    def _ai_select(self, user_input: str) -> Dict:
        """Use OpenAI to understand intent and match policy"""

        # Build product summary for context
        product_summary = self._build_product_summary()

        prompt = f"""You are Mutakamela Insurance AI assistant in Saudi Arabia.
Analyze the customer message and recommend the best insurance policy.

AVAILABLE PRODUCTS (44 total):
{product_summary}

CUSTOMER MESSAGE: "{user_input}"

Respond in this exact JSON format:
{{
    "intent": "BUY_POLICY|GET_QUOTE|GET_INFO|RENEW|CLAIM|OTHER",
    "detected_lob": "MOTOR|HEALTH|TRAVEL|PROPERTY|MARINE|LIABILITY|ENGINEERING|MISCELLANEOUS|UNKNOWN",
    "confidence": 0.0 to 1.0,
    "matched_products": [
        {{"id": "product_id", "name": "product name", "match_reason": "why this matches", "score": 0.0-1.0}}
    ],
    "entities": {{
        "vehicle_type": "",
        "coverage_preference": "",
        "budget_indicator": "",
        "family_size": "",
        "business_type": ""
    }},
    "follow_up_question": "Question to ask customer to narrow down options",
    "response_english": "Friendly response to customer in English",
    "response_arabic": "نفس الرد بالعربية"
}}

Return top 3 matching products. Be specific about why each product matches."""

        try:
            response = self.client.chat.completions.create(
                model="gpt-4o-mini",  # Use gpt-4o for better accuracy
                messages=[{"role": "user", "content": prompt}],
                temperature=0.3,
                response_format={"type": "json_object"}
            )

            result = json.loads(response.choices[0].message.content)
            result['method'] = 'ai'
            return result

        except Exception as e:
            print(f"⚠️  AI error: {e}. Falling back to rule-based.")
            return self._rule_based_select(user_input)

    def _rule_based_select(self, user_input: str) -> Dict:
        """Rule-based matching when AI is not available"""

        user_input_lower = user_input.lower()
        scores = []

        for product in self.products:
            score = 0
            matched_keywords = []

            # Check keywords
            for keyword in product['keywords']:
                if keyword.lower() in user_input_lower:
                    score += 10
                    matched_keywords.append(keyword)

            # Check product name
            if product['name'].lower() in user_input_lower:
                score += 20

            # Check LOB indicators
            lob_keywords = {
                'MOTOR': ['car', 'vehicle', 'سيارة', 'motor', 'drive'],
                'HEALTH': ['health', 'medical', 'hospital', 'صحي', 'doctor'],
                'TRAVEL': ['travel', 'trip', 'vacation', 'سفر', 'flight'],
                'PROPERTY': ['home', 'house', 'building', 'منزل', 'property'],
                'MARINE': ['ship', 'cargo', 'shipping', 'بحري', 'import'],
                'LIABILITY': ['liability', 'sue', 'مسؤولية', 'professional'],
                'ENGINEERING': ['construction', 'building', 'مقاول', 'contractor']
            }

            for lob, keywords in lob_keywords.items():
                if product['lob'] == lob:
                    for kw in keywords:
                        if kw in user_input_lower:
                            score += 15

            if score > 0:
                scores.append({
                    'id': product['id'],
                    'name': product['name'],
                    'name_ar': product['name_ar'],
                    'lob': product['lob'],
                    'score': score,
                    'matched_keywords': matched_keywords,
                    'match_reason': f"Matched keywords: {', '.join(matched_keywords)}" if matched_keywords else "LOB match"
                })

        # Sort by score
        scores.sort(key=lambda x: x['score'], reverse=True)
        top_matches = scores[:3]

        # Determine intent
        intent = "GET_INFO"
        if any(w in user_input_lower for w in ['buy', 'want', 'need', 'get', 'أريد', 'احتاج']):
            intent = "BUY_POLICY"
        elif any(w in user_input_lower for w in ['price', 'cost', 'how much', 'كم', 'سعر']):
            intent = "GET_QUOTE"
        elif any(w in user_input_lower for w in ['renew', 'تجديد']):
            intent = "RENEW"
        elif any(w in user_input_lower for w in ['claim', 'accident', 'مطالبة', 'حادث']):
            intent = "CLAIM"

        # Detect LOB
        detected_lob = "UNKNOWN"
        if top_matches:
            detected_lob = top_matches[0]['lob']

        return {
            'method': 'rule_based',
            'intent': intent,
            'detected_lob': detected_lob,
            'confidence': min(0.9, (top_matches[0]['score'] / 50)) if top_matches else 0.1,
            'matched_products': [
                {
                    'id': m['id'],
                    'name': m['name'],
                    'name_ar': m['name_ar'],
                    'match_reason': m['match_reason'],
                    'score': m['score'] / 50
                }
                for m in top_matches
            ],
            'entities': {},
            'follow_up_question': self._generate_followup(detected_lob, top_matches),
            'response_english': self._generate_response(intent, top_matches),
            'response_arabic': self._generate_response_ar(intent, top_matches)
        }

    def _build_product_summary(self) -> str:
        """Build a summary of products for the AI prompt"""
        summary = []
        for p in self.products:
            summary.append(f"- {p['id']}: {p['name']} ({p['lob']}) - {', '.join(p['keywords'][:5])}")
        return '\n'.join(summary)

    def _generate_followup(self, lob: str, matches: List) -> str:
        """Generate follow-up question based on LOB"""
        followups = {
            'MOTOR': "What type of vehicle do you have? (Car/Motorcycle/Fleet)",
            'HEALTH': "Is this for yourself, your family, or your company?",
            'TRAVEL': "Is this for a single trip or do you travel frequently?",
            'PROPERTY': "Is this for your home or a commercial property?",
            'MARINE': "Are you shipping cargo or do you need hull insurance?",
            'LIABILITY': "What type of business or profession do you have?",
            'ENGINEERING': "Is this for a construction project or equipment?",
            'UNKNOWN': "Could you tell me more about what you need insurance for?"
        }
        return followups.get(lob, followups['UNKNOWN'])

    def _generate_response(self, intent: str, matches: List) -> str:
        """Generate English response"""
        if not matches:
            return "I'd be happy to help you find the right insurance. Could you tell me more about what you need?"

        top = matches[0]
        if intent == "BUY_POLICY":
            return f"Great choice! Based on your needs, I recommend {top['name']}. Would you like a quote?"
        elif intent == "GET_QUOTE":
            return f"I can get you a quote for {top['name']}. Let me ask a few questions first."
        else:
            return f"Based on what you've told me, {top['name']} might be a good fit. Want to know more?"

    def _generate_response_ar(self, intent: str, matches: List) -> str:
        """Generate Arabic response"""
        if not matches:
            return "يسعدني مساعدتك في إيجاد التأمين المناسب. هل يمكنك إخباري بالمزيد عن احتياجاتك؟"

        top = matches[0]
        if intent == "BUY_POLICY":
            return f"اختيار ممتاز! بناءً على احتياجاتك، أنصحك بـ {top.get('name_ar', top['name'])}. هل تريد عرض سعر؟"
        elif intent == "GET_QUOTE":
            return f"يمكنني الحصول على عرض سعر لـ {top.get('name_ar', top['name'])}. دعني أسألك بعض الأسئلة أولاً."
        else:
            return f"بناءً على ما أخبرتني به، {top.get('name_ar', top['name'])} قد يكون مناسباً لك. هل تريد معرفة المزيد؟"


def print_result(result: Dict):
    """Pretty print the result"""
    print("\n" + "="*60)
    print("🤖 AI POLICY SELECTOR RESULT")
    print("="*60)
    print(f"Method: {result.get('method', 'unknown')}")
    print(f"Intent: {result['intent']}")
    print(f"Detected LOB: {result['detected_lob']}")
    print(f"Confidence: {result['confidence']:.0%}")

    print("\n📋 MATCHED PRODUCTS:")
    for i, product in enumerate(result['matched_products'], 1):
        print(f"  {i}. {product['name']} ({product['id']})")
        print(f"     Score: {product.get('score', 0):.0%}")
        print(f"     Reason: {product['match_reason']}")

    if result.get('entities'):
        print("\n🔍 EXTRACTED ENTITIES:")
        for key, value in result['entities'].items():
            if value:
                print(f"  {key}: {value}")

    print(f"\n❓ FOLLOW-UP: {result.get('follow_up_question', '')}")
    print(f"\n💬 RESPONSE (EN): {result['response_english']}")
    print(f"💬 RESPONSE (AR): {result['response_arabic']}")
    print("="*60)


def main():
    """Interactive demo"""
    print("\n" + "="*60)
    print("🏢 MUTAKAMELA AI POLICY SELECTOR")
    print("="*60)
    print("Type a message like a customer would.")
    print("Examples:")
    print("  - I need car insurance")
    print("  - أريد تأمين صحي لعائلتي")
    print("  - How much for travel insurance to Europe?")
    print("  - I want to insure my construction project")
    print("\nType 'quit' to exit.\n")

    selector = PolicySelector(use_ai=True)

    while True:
        try:
            user_input = input("👤 You: ").strip()

            if user_input.lower() in ['quit', 'exit', 'q']:
                print("👋 Goodbye!")
                break

            if not user_input:
                continue

            result = selector.select_policy(user_input)
            print_result(result)

        except KeyboardInterrupt:
            print("\n👋 Goodbye!")
            break
        except Exception as e:
            print(f"❌ Error: {e}")


if __name__ == "__main__":
    main()
