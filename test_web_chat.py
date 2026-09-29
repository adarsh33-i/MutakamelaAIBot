import unittest
import uuid
from unittest.mock import patch

import web_chat


class FakePolicySelector:
    def __init__(self):
        self.conversation_history = []

    def select_policy(self, message, lang='en'):
        self.conversation_history.extend([
            {'role': 'user', 'content': message},
            {'role': 'assistant', 'content': 'English reply'}
        ])
        return {'response': 'English reply', 'response_ar': 'Arabic reply'}

    def clear_history(self):
        self.conversation_history.clear()


class FailingPolicySelector(FakePolicySelector):
    def select_policy(self, message, lang='en'):
        return {
            '_error': True,
            'response': 'Fallback response',
            'response_ar': 'Fallback response in Arabic'
        }


class ChatApiTests(unittest.TestCase):
    def setUp(self):
        web_chat.sessions.clear()
        web_chat.limiter.reset()
        self.original_rate_limit_enabled = web_chat.app.config.get('RATELIMIT_ENABLED', True)
        web_chat.app.config['RATELIMIT_ENABLED'] = False
        self.selector_patch = patch.object(web_chat, 'ConversationalPolicySelector', FakePolicySelector)
        self.selector_patch.start()
        self.client = web_chat.app.test_client()

    def tearDown(self):
        web_chat.sessions.clear()
        web_chat.limiter.reset()
        web_chat.app.config['RATELIMIT_ENABLED'] = self.original_rate_limit_enabled
        self.selector_patch.stop()

    def test_requires_a_valid_session_id(self):
        missing = self.client.post('/api/chat', json={'message': 'hello'})
        shared_default = self.client.post('/api/chat', json={'session_id': 'default', 'message': 'hello'})

        self.assertEqual(missing.status_code, 400)
        self.assertEqual(shared_default.status_code, 400)
        self.assertEqual(web_chat.sessions, {})

    def test_restores_history_before_processing_new_message(self):
        session_id = str(uuid.uuid4())
        history = [
            {'role': 'user', 'content': 'Earlier question'},
            {'role': 'assistant', 'content': 'Earlier answer'}
        ]

        response = self.client.post('/api/chat', json={
            'session_id': session_id,
            'message': 'Follow-up',
            'history': history
        })

        self.assertEqual(response.status_code, 200)
        self.assertEqual(response.json['conversation_response'], 'English reply')
        self.assertEqual(web_chat.sessions[session_id]['ai'].conversation_history[:2], history)
        self.assertEqual(web_chat.sessions[session_id]['ai'].conversation_history[2]['content'], 'Follow-up')

    def test_arabic_display_swap_keeps_original_context_response(self):
        session_id = str(uuid.uuid4())

        response = self.client.post('/api/chat', json={
            'session_id': session_id,
            'message': 'مرحبا',
            'lang': 'ar',
            'history': []
        })

        self.assertEqual(response.status_code, 200)
        self.assertEqual(response.json['response'], 'Arabic reply')
        self.assertEqual(response.json['conversation_response'], 'English reply')

    def test_model_fallback_is_returned_as_an_error(self):
        session_id = str(uuid.uuid4())
        with patch.object(web_chat, 'ConversationalPolicySelector', FailingPolicySelector):
            response = self.client.post('/api/chat', json={
                'session_id': session_id,
                'message': 'hello',
                'history': []
            })

        self.assertEqual(response.status_code, 502)
        self.assertNotIn('_error', response.json)

    def test_rejects_invalid_message_and_history(self):
        session_id = str(uuid.uuid4())
        invalid_message = self.client.post('/api/chat', json={
            'session_id': session_id,
            'message': 'x' * (web_chat.MAX_MESSAGE_LENGTH + 1)
        })
        invalid_history = self.client.post('/api/chat', json={
            'session_id': session_id,
            'message': 'hello',
            'history': [{'role': 'system', 'content': 'not allowed'}]
        })

        self.assertEqual(invalid_message.status_code, 413)
        self.assertEqual(invalid_history.status_code, 400)
        self.assertEqual(web_chat.sessions, {})

    def test_expiry_uses_last_activity(self):
        now = 10_000
        expired_id = 'a' * 32
        active_id = 'b' * 32
        web_chat.sessions[expired_id] = {
            'ai': FakePolicySelector(),
            'created': now - web_chat.SESSION_TIMEOUT * 2,
            'last_accessed': now - web_chat.SESSION_TIMEOUT - 1
        }
        web_chat.sessions[active_id] = {
            'ai': FakePolicySelector(),
            'created': now - web_chat.SESSION_TIMEOUT * 2,
            'last_accessed': now - web_chat.SESSION_TIMEOUT + 1
        }

        web_chat.cleanup_old_sessions(now)

        self.assertNotIn(expired_id, web_chat.sessions)
        self.assertIn(active_id, web_chat.sessions)

    def test_cors_is_not_open_by_default(self):
        response = self.client.get('/', headers={'Origin': 'https://outside.example'})

        self.assertNotIn('Access-Control-Allow-Origin', response.headers)

    def test_chat_endpoint_is_rate_limited(self):
        web_chat.app.config['RATELIMIT_ENABLED'] = True
        session_id = str(uuid.uuid4())
        responses = [self.client.post('/api/chat', json={
            'session_id': session_id,
            'message': 'hello'
        }) for _ in range(21)]

        self.assertEqual(sum(response.status_code == 200 for response in responses), 20)
        self.assertEqual(responses[-1].status_code, 429)


if __name__ == '__main__':
    unittest.main()
