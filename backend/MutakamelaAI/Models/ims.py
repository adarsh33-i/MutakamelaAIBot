from __future__ import annotations

from dataclasses import dataclass, field
from typing import Any, Dict, Optional


@dataclass
class ReviewSummary:
    application_id: str
    flow_type: str
    customer_name: str
    product_type: str
    summary: Dict[str, Any] = field(default_factory=dict)
    premium: Optional[float] = None
    requires_confirmation: bool = True


@dataclass
class ImsSubmissionRequest:
    application_id: str
    flow_type: str
    ims_route: str
    payload: Dict[str, Any] = field(default_factory=dict)
    submission_id: Optional[str] = None
    submitted: bool = False
