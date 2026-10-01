from __future__ import annotations

from typing import Any, Dict, Optional

from Models.application import InsuranceApplication, WorkflowStatus
from Models.ims import ImsSubmissionRequest, ReviewSummary


class InsuranceWorkflowService:
    def build_review_payload(self, application: InsuranceApplication) -> Dict[str, Any]:
        customer_name = " ".join(
            filter(
                None,
                [
                    application.customer_data.get("first_name"),
                    application.customer_data.get("last_name"),
                ],
            )
        )

        premium = None
        if application.flow_type.value == "policy" and isinstance(application.policy_data, dict):
            premium = application.policy_data.get("premium")

        review = ReviewSummary(
            application_id=application.application_id,
            flow_type=application.flow_type.value,
            customer_name=customer_name,
            product_type=application.product_type,
            premium=premium,
            summary={
                "customer": application.customer_data,
                "product": application.policy_data or application.claim_data,
                "documents": application.documents_status,
            },
            requires_confirmation=True,
        )

        return {
            "application_id": review.application_id,
            "flow_type": review.flow_type,
            "customer_name": review.customer_name,
            "product_type": review.product_type,
            "premium": review.premium,
            "requires_confirmation": review.requires_confirmation,
            "summary": review.summary,
        }

    def submit_to_ims(self, application: InsuranceApplication) -> Dict[str, Any]:
        if not application.is_customer_confirmed:
            return {
                "status": "waiting_for_confirmation",
                "submitted": False,
                "message": "Customer confirmation is required before IMS handoff.",
            }

        review_payload = self.build_review_payload(application)
        ims_request = ImsSubmissionRequest(
            application_id=application.application_id,
            flow_type=application.flow_type.value,
            ims_route=f"/ims/{application.flow_type.value}/{application.product_type}",
            payload=review_payload,
            submitted=True,
        )

        return {
            "status": "submitted",
            "submitted": ims_request.submitted,
            "application_id": ims_request.application_id,
            "flow_type": ims_request.flow_type,
            "ims_route": ims_request.ims_route,
            "payload": ims_request.payload,
        }

    def evaluate_application(self, application: InsuranceApplication) -> Dict[str, Any]:
        if not application.has_required_customer_data() or not application.has_required_product_data():
            application.status = WorkflowStatus.COLLECTING_DATA
            return {
                "status": WorkflowStatus.COLLECTING_DATA.value,
                "can_submit_to_ims": False,
                "message": "Customer or product details are incomplete.",
            }

        if application.has_missing_documents():
            application.status = WorkflowStatus.AWAITING_DOCUMENTS
            return {
                "status": WorkflowStatus.AWAITING_DOCUMENTS.value,
                "can_submit_to_ims": False,
                "message": "Required documents are missing or pending approval.",
            }

        if not application.is_customer_confirmed:
            application.status = WorkflowStatus.READY_FOR_REVIEW
            return {
                "status": WorkflowStatus.READY_FOR_REVIEW.value,
                "can_submit_to_ims": False,
                "message": "Review summary is ready. Customer confirmation is required before IMS submission.",
            }

        application.status = WorkflowStatus.APPROVED_FOR_SUBMISSION
        return {
            "status": WorkflowStatus.APPROVED_FOR_SUBMISSION.value,
            "can_submit_to_ims": True,
            "message": "The request is ready for IMS submission.",
        }

    def confirm_submission(self, application: InsuranceApplication) -> Dict[str, Any]:
        if not application.has_required_customer_data() or not application.has_required_product_data():
            application.status = WorkflowStatus.COLLECTING_DATA
            return {
                "status": WorkflowStatus.COLLECTING_DATA.value,
                "can_submit_to_ims": False,
                "message": "The request is incomplete and cannot be submitted.",
            }

        if application.has_missing_documents():
            application.status = WorkflowStatus.AWAITING_DOCUMENTS
            return {
                "status": WorkflowStatus.AWAITING_DOCUMENTS.value,
                "can_submit_to_ims": False,
                "message": "Required documents must be approved before submission.",
            }

        application.is_customer_confirmed = True
        application.status = WorkflowStatus.APPROVED_FOR_SUBMISSION
        return {
            "status": WorkflowStatus.APPROVED_FOR_SUBMISSION.value,
            "can_submit_to_ims": True,
            "message": "Customer confirmation received. The request is approved for submission.",
        }
