from __future__ import annotations

from dataclasses import dataclass, field
from enum import Enum
from typing import Any, Dict, List, Optional


class FlowType(str, Enum):
    POLICY = "policy"
    CLAIM = "claim"
    AMENDMENT = "amendment"


class WorkflowStatus(str, Enum):
    INITIATED = "initiated"
    COLLECTING_DATA = "collecting_data"
    VALIDATING_DATA = "validating_data"
    AWAITING_DOCUMENTS = "awaiting_documents"
    READY_FOR_REVIEW = "ready_for_review"
    AWAITING_CUSTOMER_CONFIRMATION = "awaiting_customer_confirmation"
    APPROVED_FOR_SUBMISSION = "approved_for_submission"
    SUBMITTED = "submitted"
    FAILED = "failed"
    REQUIRES_MANUAL_REVIEW = "requires_manual_review"


@dataclass
class InsuranceApplication:
    application_id: str
    flow_type: FlowType
    product_type: str
    customer_data: Dict[str, Any] = field(default_factory=dict)
    policy_data: Dict[str, Any] = field(default_factory=dict)
    claim_data: Dict[str, Any] = field(default_factory=dict)
    required_documents: List[str] = field(default_factory=list)
    documents_status: Dict[str, str] = field(default_factory=dict)
    is_customer_confirmed: bool = False
    status: WorkflowStatus = WorkflowStatus.INITIATED
    notes: Optional[str] = None

    def has_required_customer_data(self) -> bool:
        return bool(
            self.customer_data.get("first_name")
            and self.customer_data.get("last_name")
            and self.customer_data.get("nationality")
        )

    def has_required_product_data(self) -> bool:
        if self.flow_type == FlowType.POLICY:
            return bool(self.policy_data.get("coverage_type"))
        if self.flow_type == FlowType.CLAIM:
            return bool(self.claim_data.get("claim_reason"))
        return True

    def has_all_required_documents(self) -> bool:
        if not self.required_documents:
            return True
        return all(self.documents_status.get(doc_name) == "approved" for doc_name in self.required_documents)

    def has_missing_documents(self) -> bool:
        if not self.required_documents:
            return False
        return any(
            self.documents_status.get(doc_name) not in ("approved", "valid")
            for doc_name in self.required_documents
        )

    def is_ready_for_review(self) -> bool:
        return self.has_required_customer_data() and self.has_required_product_data() and self.has_all_required_documents()
