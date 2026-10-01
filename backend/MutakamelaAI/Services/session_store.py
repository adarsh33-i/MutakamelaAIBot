from __future__ import annotations

from typing import Any, Dict, Optional


class ApplicationSessionStore:
    def __init__(self) -> None:
        self._sessions: Dict[str, Dict[str, Any]] = {}

    def create_session(self, application_id: str, initial_data: Optional[Dict[str, Any]] = None) -> Dict[str, Any]:
        session = {"application_id": application_id, "data": initial_data or {}} 
        self._sessions[application_id] = session
        return session

    def get_session(self, application_id: str) -> Optional[Dict[str, Any]]:
        return self._sessions.get(application_id)

    def update_session(self, application_id: str, **kwargs: Any) -> Dict[str, Any]:
        session = self._sessions.setdefault(application_id, {"application_id": application_id, "data": {}})
        session.update(kwargs)
        return session

    def delete_session(self, application_id: str) -> None:
        self._sessions.pop(application_id, None)
