from Models.application import InsuranceApplication, FlowType
from Services.workflow_service import InsuranceWorkflowService


def test_review_payload_is_built_before_submission():
    app = InsuranceApplication(
        application_id="APP-2001",
        flow_type=FlowType.POLICY,
        product_type="motor",
        customer_data={"first_name": "Ali", "last_name": "Ahmed", "nationality": "KSA"},
        policy_data={"coverage_type": "comprehensive", "premium": 520.0},
        required_documents=["id_copy"],
        documents_status={"id_copy": "approved"},
        is_customer_confirmed=True,
    )

    service = InsuranceWorkflowService()
    summary = service.build_review_payload(app)

    assert summary["application_id"] == "APP-2001"
    assert summary["flow_type"] == "policy"
    assert summary["customer_name"] == "Ali Ahmed"
    assert summary["premium"] == 520.0
    assert summary["requires_confirmation"] is True


def test_ims_handoff_is_blocked_until_customer_confirms():
    app = InsuranceApplication(
        application_id="APP-2002",
        flow_type=FlowType.CLAIM,
        product_type="motor",
        customer_data={"first_name": "Sara", "last_name": "Khan", "nationality": "KSA"},
        claim_data={"claim_reason": "accident"},
        required_documents=["police_report"],
        documents_status={"police_report": "approved"},
        is_customer_confirmed=False,
    )

    service = InsuranceWorkflowService()
    result = service.submit_to_ims(app)

    assert result["status"] == "waiting_for_confirmation"
    assert result["submitted"] is False


def test_policy_requires_customer_confirmation_before_automation():
    app = InsuranceApplication(
        application_id="APP-1001",
        flow_type=FlowType.POLICY,
        product_type="motor",
        customer_data={"first_name": "Ali", "last_name": "Ahmed", "nationality": "KSA"},
        policy_data={"coverage_type": "comprehensive"},
        required_documents=["id_copy"],
        documents_status={"id_copy": "approved"},
        is_customer_confirmed=False,
    )

    service = InsuranceWorkflowService()
    result = service.evaluate_application(app)

    assert result["status"] == "ready_for_review"
    assert result["can_submit_to_ims"] is False


def test_valid_policy_flow_reaches_approval_gate():
    app = InsuranceApplication(
        application_id="APP-1002",
        flow_type=FlowType.POLICY,
        product_type="motor",
        customer_data={"first_name": "Sara", "last_name": "Khan", "nationality": "KSA"},
        policy_data={"coverage_type": "comprehensive"},
        required_documents=["id_copy", "vehicle_registration"],
        documents_status={"id_copy": "approved", "vehicle_registration": "approved"},
        is_customer_confirmed=False,
    )

    service = InsuranceWorkflowService()
    result = service.evaluate_application(app)

    assert result["status"] == "ready_for_review"
    assert result["can_submit_to_ims"] is False

    app.is_customer_confirmed = True
    approved = service.confirm_submission(app)

    assert approved["status"] == "approved_for_submission"
    assert approved["can_submit_to_ims"] is True


def test_claim_requires_documents_before_approval():
    app = InsuranceApplication(
        application_id="APP-1003",
        flow_type=FlowType.CLAIM,
        product_type="motor",
        customer_data={"first_name": "Nora", "last_name": "Smith", "nationality": "KSA"},
        claim_data={"claim_reason": "accident"},
        required_documents=["police_report", "claim_form"],
        documents_status={"police_report": "missing", "claim_form": "approved"},
        is_customer_confirmed=True,
    )

    service = InsuranceWorkflowService()
    result = service.evaluate_application(app)

    assert result["status"] == "awaiting_documents"
    assert result["can_submit_to_ims"] is False
