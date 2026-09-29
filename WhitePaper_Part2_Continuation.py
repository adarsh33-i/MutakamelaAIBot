#!/usr/bin/env python3
"""
Continuation of WhitePaper - Sections 8-14 + Appendix
To be added after Section 7 in WhitePaper 2
"""

import zipfile

CONTENT_TYPES = '''<?xml version="1.0" encoding="UTF-8" standalone="yes"?>
<Types xmlns="http://schemas.openxmlformats.org/package/2006/content-types">
  <Default Extension="rels" ContentType="application/vnd.openxmlformats-package.relationships+xml"/>
  <Default Extension="xml" ContentType="application/xml"/>
  <Override PartName="/word/document.xml" ContentType="application/vnd.openxmlformats-officedocument.wordprocessingml.document.main+xml"/>
  <Override PartName="/word/styles.xml" ContentType="application/vnd.openxmlformats-officedocument.wordprocessingml.styles+xml"/>
</Types>'''

RELS = '''<?xml version="1.0" encoding="UTF-8" standalone="yes"?>
<Relationships xmlns="http://schemas.openxmlformats.org/package/2006/relationships">
  <Relationship Id="rId1" Type="http://schemas.openxmlformats.org/officeDocument/2006/relationships/officeDocument" Target="word/document.xml"/>
</Relationships>'''

WORD_RELS = '''<?xml version="1.0" encoding="UTF-8" standalone="yes"?>
<Relationships xmlns="http://schemas.openxmlformats.org/package/2006/relationships">
  <Relationship Id="rId1" Type="http://schemas.openxmlformats.org/officeDocument/2006/relationships/styles" Target="styles.xml"/>
</Relationships>'''

STYLES = '''<?xml version="1.0" encoding="UTF-8" standalone="yes"?>
<w:styles xmlns:w="http://schemas.openxmlformats.org/wordprocessingml/2006/main">
  <w:style w:type="paragraph" w:styleId="Normal" w:default="1">
    <w:name w:val="Normal"/>
    <w:rPr><w:sz w:val="22"/><w:szCs w:val="22"/></w:rPr>
  </w:style>
</w:styles>'''

def escape(text):
    return str(text).replace("&", "&amp;").replace("<", "&lt;").replace(">", "&gt;")

def p(text, bold=False, italic=False, size=22, color=None, center=False):
    props = '<w:spacing w:after="150"/>'
    if center:
        props += '<w:jc w:val="center"/>'
    rprops = f'<w:sz w:val="{size}"/><w:szCs w:val="{size}"/>'
    if bold:
        rprops += '<w:b/>'
    if italic:
        rprops += '<w:i/>'
    if color:
        rprops += f'<w:color w:val="{color}"/>'
    return f'<w:p><w:pPr>{props}</w:pPr><w:r><w:rPr>{rprops}</w:rPr><w:t xml:space="preserve">{escape(text)}</w:t></w:r></w:p>'

def h1(text):
    return f'<w:p><w:pPr><w:spacing w:before="300" w:after="200"/></w:pPr><w:r><w:rPr><w:b/><w:sz w:val="32"/><w:szCs w:val="32"/><w:color w:val="1F4E79"/></w:rPr><w:t>{escape(text)}</w:t></w:r></w:p>'

def h2(text):
    return f'<w:p><w:pPr><w:spacing w:before="240" w:after="120"/></w:pPr><w:r><w:rPr><w:b/><w:sz w:val="26"/><w:szCs w:val="26"/><w:color w:val="2E74B5"/></w:rPr><w:t>{escape(text)}</w:t></w:r></w:p>'

def h3(text):
    return f'<w:p><w:pPr><w:spacing w:before="200" w:after="100"/></w:pPr><w:r><w:rPr><w:b/><w:sz w:val="24"/><w:szCs w:val="24"/><w:color w:val="404040"/></w:rPr><w:t>{escape(text)}</w:t></w:r></w:p>'

def bullet(text):
    return f'<w:p><w:pPr><w:spacing w:after="80"/><w:ind w:left="720" w:hanging="360"/></w:pPr><w:r><w:rPr><w:sz w:val="22"/></w:rPr><w:t xml:space="preserve">* {escape(text)}</w:t></w:r></w:p>'

def numbered(text, num):
    return f'<w:p><w:pPr><w:spacing w:after="80"/><w:ind w:left="720" w:hanging="360"/></w:pPr><w:r><w:rPr><w:sz w:val="22"/><w:b/></w:rPr><w:t>{num}. </w:t></w:r><w:r><w:rPr><w:sz w:val="22"/></w:rPr><w:t>{escape(text)}</w:t></w:r></w:p>'

def table(headers, rows, col_widths=None):
    num_cols = len(headers)
    if col_widths is None:
        col_widths = [9500 // num_cols] * num_cols
    total_width = sum(col_widths)
    grid = ''.join([f'<w:gridCol w:w="{w}"/>' for w in col_widths])

    t = f'''<w:tbl>
<w:tblPr>
<w:tblW w:w="{total_width}" w:type="dxa"/>
<w:tblBorders>
<w:top w:val="single" w:sz="4" w:color="1F4E79"/>
<w:left w:val="single" w:sz="4" w:color="1F4E79"/>
<w:bottom w:val="single" w:sz="4" w:color="1F4E79"/>
<w:right w:val="single" w:sz="4" w:color="1F4E79"/>
<w:insideH w:val="single" w:sz="4" w:color="BDD7EE"/>
<w:insideV w:val="single" w:sz="4" w:color="BDD7EE"/>
</w:tblBorders>
<w:tblLayout w:type="fixed"/>
</w:tblPr>
<w:tblGrid>{grid}</w:tblGrid>'''

    t += '<w:tr>'
    for i, h in enumerate(headers):
        w = col_widths[i]
        t += f'<w:tc><w:tcPr><w:tcW w:w="{w}" w:type="dxa"/><w:shd w:val="clear" w:fill="1F4E79"/></w:tcPr><w:p><w:r><w:rPr><w:b/><w:sz w:val="20"/><w:color w:val="FFFFFF"/></w:rPr><w:t>{escape(h)}</w:t></w:r></w:p></w:tc>'
    t += '</w:tr>'

    for row in rows:
        t += '<w:tr>'
        for i, cell in enumerate(row):
            w = col_widths[i] if i < len(col_widths) else col_widths[-1]
            t += f'<w:tc><w:tcPr><w:tcW w:w="{w}" w:type="dxa"/></w:tcPr><w:p><w:r><w:rPr><w:sz w:val="20"/></w:rPr><w:t>{escape(cell)}</w:t></w:r></w:p></w:tc>'
        t += '</w:tr>'

    t += '</w:tbl>'
    return t

def hr():
    return '<w:p><w:pPr><w:pBdr><w:bottom w:val="single" w:sz="6" w:color="1F4E79"/></w:pBdr></w:pPr></w:p>'

def pagebreak():
    return '<w:p><w:r><w:br w:type="page"/></w:r></w:p>'

content = []

# INSTRUCTION
content.append(p("CONTINUATION - ADD THIS AFTER SECTION 7 IN WHITEPAPER 2", bold=True, size=28, center=True, color="C00000"))
content.append(hr())
content.append(pagebreak())

# 8. FRAUD DETECTION
content.append(h1("8. Fraud Detection and Risk Assessment"))
content.append(p("Protect Before, During, and After", italic=True, size=24, color="666666"))
content.append(hr())
content.append(h2("8.1 Real-Time Fraud Detection"))
content.append(p("Every transaction is scored for fraud risk in real-time:"))
content.append(table(
    ["Detection Type", "Technique", "Red Flags"],
    [
        ["Application Fraud", "NLP + Anomaly Detection", "Inconsistent answers, fabricated history"],
        ["Document Fraud", "Computer Vision + OCR", "Photoshopped docs, altered dates"],
        ["Claims Fraud", "Pattern Recognition + ML", "Staged accidents, inflated claims"],
        ["Identity Fraud", "Biometric + Database Check", "Stolen IDs, impersonation"],
        ["Collusion", "Network Analysis + Graph ML", "Related parties, coordinated claims"],
    ],
    col_widths=[2500, 3000, 4000]
))
content.append(h2("8.2 Fraud Risk Score"))
content.append(p("0-100 score determines processing path:"))
content.append(table(
    ["Score", "Risk Level", "Action"],
    [
        ["0-30", "Low", "Auto-approve, standard processing"],
        ["31-60", "Medium", "Enhanced verification, additional questions"],
        ["61-80", "High", "Manual review required, hold processing"],
        ["81-100", "Critical", "Block transaction, escalate to SIU"],
    ],
    col_widths=[1500, 2500, 5500]
))
content.append(h2("8.3 AI Underwriting Engine"))
content.append(p("Instant risk assessment for automated decisions:"))
content.append(table(
    ["Decision", "Criteria", "Response Time"],
    [
        ["Auto-Accept", "Standard risk, complete data, no flags", "Less than 5 seconds"],
        ["Auto-Quote", "Moderate risk, pricing adjustment needed", "Less than 30 seconds"],
        ["Refer to Underwriter", "High-value, complex, missing data", "Queued for review"],
        ["Decline", "Uninsurable risk, fraud flags", "Instant with explanation"],
    ],
    col_widths=[2500, 4500, 2500]
))
content.append(h2("8.4 LOB-Specific Fraud Indicators"))
content.append(table(
    ["LOB", "Key Indicators"],
    [
        ["Motor", "Pre-existing damage, staged collisions, phantom passengers, inflated repair costs"],
        ["Health", "Phantom billing, upcoding procedures, doctor shopping, prescription fraud"],
        ["Travel", "Post-departure claims, fake cancellations, duplicate claims across insurers"],
        ["Marine", "Over-valuation, fictitious cargo, scuttling, backdated policies"],
        ["Property", "Arson indicators, inflated inventory, fake burglary, concealed prior damage"],
    ],
    col_widths=[2000, 7500]
))
content.append(pagebreak())

# 9. ANALYTICS
content.append(h1("9. Predictive Analytics and Business Intelligence"))
content.append(p("Data-Driven Decision Making", italic=True, size=24, color="666666"))
content.append(hr())
content.append(h2("9.1 Claims Prediction"))
content.append(table(
    ["Prediction", "Model", "Business Value"],
    [
        ["Claim Frequency", "Time-series forecasting", "Reserve planning, reinsurance negotiation"],
        ["Claim Severity", "Regression models", "Loss ratio prediction, pricing adjustment"],
        ["Settlement Amount", "XGBoost ensemble", "Early reserve setting, negotiation guidance"],
        ["Litigation Risk", "NLP on claim notes", "Early legal intervention, settlement strategy"],
        ["Subrogation Potential", "Rule engine + ML", "Recovery opportunity identification"],
    ],
    col_widths=[2500, 3000, 4000]
))
content.append(h2("9.2 Customer Analytics"))
content.append(table(
    ["Metric", "Capability", "Action"],
    [
        ["Churn Prediction", "Behavioral + transactional analysis", "Proactive retention offers"],
        ["Cross-sell Propensity", "Product affinity modeling", "Personalized recommendations"],
        ["Lifetime Value", "Revenue + retention modeling", "Tiered service levels"],
        ["Renewal Probability", "Historical patterns + triggers", "Campaign timing optimization"],
    ],
    col_widths=[2800, 3200, 3500]
))
content.append(h2("9.3 Operational Dashboard"))
content.append(bullet("Real-time policy issuance metrics by LOB, channel, agent"))
content.append(bullet("Claims processing funnel visualization"))
content.append(bullet("Agent/branch performance scorecards"))
content.append(bullet("Channel effectiveness (WhatsApp vs App vs Web)"))
content.append(bullet("Revenue and loss ratio trending"))
content.append(h2("9.4 Automated Reporting"))
content.append(table(
    ["Report", "Frequency", "Audience"],
    [
        ["Production Dashboard", "Real-time", "Operations"],
        ["Claims Analysis", "Daily", "Claims team"],
        ["Fraud Alerts", "Real-time", "SIU"],
        ["SAMA Compliance", "Monthly/Quarterly", "Compliance"],
        ["Executive Summary", "Weekly", "Leadership"],
    ],
    col_widths=[3000, 2500, 4000]
))
content.append(pagebreak())

# PART III DIVIDER
content.append(p("PART III", bold=True, size=36, center=True, color="1F4E79"))
content.append(p("IMPLEMENTATION", bold=True, size=44, center=True, color="2E74B5"))
content.append(pagebreak())

# 10. CUSTOMER JOURNEYS
content.append(h1("10. End-to-End Customer Journeys"))
content.append(p("Complete Flows with Instant PDF Delivery", italic=True, size=24, color="666666"))
content.append(hr())
content.append(h2("10.1 Motor Insurance Journey"))
content.append(p("Customer: I need car insurance for my new Camry"))
content.append(table(
    ["Step", "Time", "Action", "System"],
    [
        ["1", "0:00", "Customer clicks WhatsApp button on website", "Entry"],
        ["2", "0:05", "AI asks: Comprehensive or Third Party?", "AI Engine"],
        ["3", "0:15", "Customer: Comprehensive with agency repair", "Chat"],
        ["4", "0:20", "AI: Plate number please?", "AI Engine"],
        ["5", "0:30", "Customer provides plate number", "Chat"],
        ["6", "0:32", "AI fetches vehicle details from Elm", "API"],
        ["7", "0:35", "AI: Toyota Camry 2024. SAR 2,850/year. Proceed?", "Quote"],
        ["8", "0:40", "Customer: Yes", "Chat"],
        ["9", "0:45", "AI sends payment link", "Payment"],
        ["10", "1:30", "Payment completed", "Gateway"],
        ["11", "1:35", "AI: Upload Istimara photo", "AI Engine"],
        ["12", "2:00", "Customer uploads, OCR processes", "Document AI"],
        ["13", "2:03", "Policy created in core system", "Backend"],
        ["14", "2:05", "PDF generated with QR code", "PDF Engine"],
        ["15", "2:07", "Policy PDF sent via WhatsApp", "Delivery"],
    ],
    col_widths=[800, 900, 5000, 2800]
))
content.append(p("Total: 2 minutes 7 seconds from first message to policy in hand", bold=True, color="538135", center=True))
content.append(h2("10.2 Pre-filled Application Data"))
content.append(p("AI extracts from conversation - customer does not re-enter:"))
content.append(table(
    ["Field", "Source", "Method"],
    [
        ["Vehicle Make/Model", "new Camry", "NLP extraction"],
        ["Coverage Type", "comprehensive", "Intent classification"],
        ["Repair Preference", "agency repair", "Entity extraction"],
        ["Vehicle Details", "Plate number", "Elm API lookup"],
        ["Customer Name/ID", "Istimara photo", "OCR extraction"],
    ],
    col_widths=[2500, 3000, 4000]
))
content.append(h2("10.3 Documents Generated"))
content.append(table(
    ["Document", "Format", "Delivery"],
    [
        ["Policy Schedule", "PDF (Arabic/English)", "WhatsApp + Email"],
        ["Policy Wording", "PDF", "Email + Portal"],
        ["Insurance Certificate", "PDF with QR", "WhatsApp"],
        ["Payment Receipt", "PDF", "WhatsApp + Email"],
        ["Welcome Letter", "PDF", "Email"],
    ],
    col_widths=[3000, 3000, 3500]
))
content.append(pagebreak())

# 11. TECHNICAL ARCHITECTURE
content.append(h1("11. Technical Architecture"))
content.append(p("Scalable, Secure, Saudi-Hosted", italic=True, size=24, color="666666"))
content.append(hr())
content.append(h2("11.1 Technology Stack"))
content.append(table(
    ["Layer", "Technology", "Purpose"],
    [
        ["AI/NLP", "Arabic-BERT, AraGPT, Rasa", "Intent, Entity, Dialogue"],
        ["ML Platform", "TensorFlow, PyTorch", "Fraud, Risk models"],
        ["Backend", "Python FastAPI", "APIs, Business Logic"],
        ["Database", "PostgreSQL + Redis + MongoDB", "Data + Cache + Documents"],
        ["Document AI", "Tesseract + LayoutLM", "OCR, Field extraction"],
        ["PDF Engine", "Jinja2 + WeasyPrint", "Template rendering"],
        ["Messaging", "WhatsApp Business API", "Customer channel"],
        ["Queue", "Apache Kafka", "Async processing"],
        ["Analytics", "Apache Spark, Superset", "Big data, Dashboards"],
        ["Cloud", "AWS Middle East (Bahrain)", "Saudi data residency"],
    ],
    col_widths=[2000, 3500, 4000]
))
content.append(h2("11.2 Performance Targets"))
content.append(table(
    ["Operation", "Target", "SLA"],
    [
        ["Intent Classification", "Less than 100ms", "99.9%"],
        ["Quote Calculation", "Less than 500ms", "99.5%"],
        ["Document OCR", "Less than 3 seconds", "99%"],
        ["PDF Generation", "Less than 2 seconds", "99.9%"],
        ["WhatsApp Delivery", "Less than 2 seconds", "99%"],
        ["End-to-End (Quote to PDF)", "Less than 5 minutes", "95%"],
    ],
    col_widths=[3500, 3000, 3000]
))
content.append(h2("11.3 Security and Compliance"))
content.append(bullet("Data Encryption: AES-256 at rest, TLS 1.3 in transit"))
content.append(bullet("Saudi Data Residency: AWS Bahrain region"))
content.append(bullet("SAMA Compliance: Automated reporting, audit trails"))
content.append(bullet("PCI-DSS: Payment data isolation"))
content.append(bullet("PDPL Ready: Consent management, data deletion"))
content.append(pagebreak())

# 12. INTEGRATION
content.append(h1("12. Integration and Compliance"))
content.append(p("Connected to the Saudi Insurance Ecosystem", italic=True, size=24, color="666666"))
content.append(hr())
content.append(h2("12.1 External Integrations"))
content.append(table(
    ["System", "Purpose", "Data Exchange"],
    [
        ["Elm (Vehicle)", "Vehicle registration lookup", "Plate to Make/Model/Year/Owner"],
        ["Najm", "Motor insurance registry", "Policy registration, accident lookup"],
        ["Yakeen", "Citizen/resident verification", "ID to Name/DOB/Nationality"],
        ["Absher", "Government ID validation", "ID authenticity check"],
        ["SADAD", "Bill payment", "Premium collection"],
        ["SAMA", "Regulatory reporting", "Compliance submissions"],
        ["Chi (Health)", "Health network verification", "Provider lookup, eligibility"],
        ["Banks", "Payment processing", "Card, Apple Pay, STC Pay"],
    ],
    col_widths=[2200, 3300, 4000]
))
content.append(h2("12.2 SAMA Compliance Automation"))
content.append(table(
    ["Requirement", "Automation", "Output"],
    [
        ["Policy Documentation", "Auto-generated with disclosures", "Compliant wordings"],
        ["Premium Calculations", "Tariff validation", "Audit trail"],
        ["Claims Reporting", "Auto-classification, timeline", "Regulatory reports"],
        ["Customer Complaints", "Auto-logging, escalation", "Complaint register"],
        ["Financial Reporting", "Data aggregation, validation", "SAMA submissions"],
        ["AML/KYC", "ID verification, screening", "Compliance certificates"],
    ],
    col_widths=[2800, 3200, 3500]
))
content.append(h2("12.3 Audit and Transparency"))
content.append(bullet("Complete logging of all AI decisions with explanations"))
content.append(bullet("Explainable AI (XAI) for underwriting and claims"))
content.append(bullet("Document version control and retention (7 years)"))
content.append(bullet("Data lineage tracking for all customer information"))
content.append(bullet("Consent management per PDPL requirements"))
content.append(pagebreak())

# 13. ROADMAP
content.append(h1("13. Implementation Roadmap"))
content.append(p("26-Week Phased Delivery", italic=True, size=24, color="666666"))
content.append(hr())
content.append(h2("13.1 Phase Overview"))
content.append(table(
    ["Phase", "Weeks", "Focus", "Deliverables"],
    [
        ["1. Foundation", "1-4", "Core AI + WhatsApp", "NLU models, API setup, WhatsApp integration"],
        ["2. Motor Launch", "5-10", "Motor insurance live", "Full Motor flow, PDF generation, Najm"],
        ["3. Health + Travel", "11-16", "Expand LOBs", "Health enrollment, Travel instant quotes"],
        ["4. Commercial", "17-20", "B2B products", "Marine, Engineering, Property flows"],
        ["5. Intelligence", "21-24", "Advanced AI", "Fraud detection, Predictive analytics"],
        ["6. Optimization", "25-26", "Performance", "Arabic fine-tuning, Scale testing"],
    ],
    col_widths=[2000, 1200, 2500, 3800]
))
content.append(h2("13.2 Key Milestones"))
content.append(table(
    ["Milestone", "Week", "Success Criteria"],
    [
        ["First WhatsApp Quote", "Week 6", "Customer receives quote via WhatsApp"],
        ["First Policy via WhatsApp", "Week 8", "Complete purchase flow working"],
        ["Motor Go-Live", "Week 10", "Open to all Motor customers"],
        ["Health Go-Live", "Week 14", "Corporate enrollment working"],
        ["All LOBs Live", "Week 22", "44 products available"],
        ["Full Platform Launch", "Week 26", "All features, optimized"],
    ],
    col_widths=[3000, 1500, 5000]
))
content.append(pagebreak())

# 14. INVESTMENT
content.append(h1("14. Investment and Return on Investment"))
content.append(p("Business Case Financials", italic=True, size=24, color="666666"))
content.append(hr())
content.append(h2("14.1 Investment Summary"))
content.append(table(
    ["Category", "Year 1", "Year 2", "Year 3"],
    [
        ["Platform Development", "SAR 2,800,000", "-", "-"],
        ["Infrastructure (Cloud)", "SAR 360,000", "SAR 400,000", "SAR 450,000"],
        ["AI Model Training", "SAR 200,000", "SAR 100,000", "SAR 100,000"],
        ["WhatsApp API Costs", "SAR 120,000", "SAR 180,000", "SAR 250,000"],
        ["Integration and APIs", "SAR 150,000", "SAR 100,000", "SAR 100,000"],
        ["Support and Maintenance", "SAR 200,000", "SAR 300,000", "SAR 350,000"],
        ["TOTAL", "SAR 3,830,000", "SAR 1,080,000", "SAR 1,250,000"],
    ],
    col_widths=[3000, 2200, 2200, 2100]
))
content.append(h2("14.2 Cost Savings"))
content.append(table(
    ["Area", "Current Cost", "With AI", "Annual Savings"],
    [
        ["Policy Issuance", "SAR 150/policy", "SAR 30/policy", "SAR 6M (50K policies)"],
        ["Customer Service", "SAR 20/call", "SAR 2/chat", "SAR 3.6M (200K calls)"],
        ["Document Processing", "SAR 40/app", "SAR 5/app", "SAR 1.75M (50K apps)"],
        ["Fraud Losses", "5% loss ratio", "3% loss ratio", "SAR 4M (est.)"],
        ["Compliance Reporting", "SAR 500K/year", "SAR 100K/year", "SAR 400K"],
        ["TOTAL SAVINGS", "-", "-", "SAR 15.75M/year"],
    ],
    col_widths=[2500, 2200, 2200, 2600]
))
content.append(h2("14.3 Revenue Impact"))
content.append(table(
    ["Driver", "Impact", "Revenue"],
    [
        ["Conversion Improvement", "+15% quote-to-policy", "SAR 4.5M"],
        ["24/7 Availability", "+20% after-hours sales", "SAR 3M"],
        ["Cross-sell via AI", "+10% policies per customer", "SAR 2.5M"],
        ["Reduced Churn", "-5% lapse rate", "SAR 2M"],
        ["New Segments", "Digital-first customers", "SAR 3M"],
        ["TOTAL REVENUE IMPACT", "-", "SAR 15M/year"],
    ],
    col_widths=[3000, 3000, 3500]
))
content.append(h2("14.4 ROI Summary"))
content.append(table(
    ["Metric", "Value"],
    [
        ["Total 3-Year Investment", "SAR 6.16M"],
        ["Annual Benefits (Savings + Revenue)", "SAR 30.75M"],
        ["3-Year Net Benefit", "SAR 86M+"],
        ["ROI", "280% - 350%"],
        ["Payback Period", "8-10 months"],
    ],
    col_widths=[5000, 4500]
))
content.append(p("The AI platform pays for itself within the first year.", bold=True, center=True, size=24, color="538135"))
content.append(pagebreak())

# APPENDICES
content.append(p("APPENDICES", bold=True, size=36, center=True, color="1F4E79"))
content.append(pagebreak())

content.append(h1("Appendix A: Complete LOB Coverage"))
content.append(hr())
content.append(h2("44 Products Across 7 Lines of Business"))

content.append(h3("Motor Insurance (7 Products)"))
content.append(bullet("Third Party Liability (TPL)"))
content.append(bullet("Comprehensive - Agency Repair"))
content.append(bullet("Comprehensive - Workshop Repair"))
content.append(bullet("Takaful Motor"))
content.append(bullet("Fleet Insurance"))
content.append(bullet("Commercial Vehicle"))
content.append(bullet("Motorcycle"))

content.append(h3("Health Insurance (8 Products)"))
content.append(bullet("Individual Health"))
content.append(bullet("Family Health"))
content.append(bullet("SME Group Health"))
content.append(bullet("Corporate Group Health"))
content.append(bullet("Domestic Worker Health"))
content.append(bullet("Visitor Health"))
content.append(bullet("Student Health"))
content.append(bullet("Senior Citizen Health"))

content.append(h3("Travel Insurance (5 Products)"))
content.append(bullet("Single Trip"))
content.append(bullet("Multi-Trip Annual"))
content.append(bullet("Family Travel"))
content.append(bullet("Schengen Visa"))
content.append(bullet("Umrah/Hajj"))

content.append(h3("Protection and Savings (6 Products)"))
content.append(bullet("Term Life"))
content.append(bullet("Whole Life"))
content.append(bullet("Waad Al Ajyal (Education Savings)"))
content.append(bullet("Waad Al Hayat (Life Events)"))
content.append(bullet("Personal Accident"))
content.append(bullet("Critical Illness"))

content.append(h3("Marine Insurance (6 Products)"))
content.append(bullet("Marine Cargo (Single Shipment)"))
content.append(bullet("Marine Cargo (Open Policy)"))
content.append(bullet("Marine Hull"))
content.append(bullet("Protection and Indemnity"))
content.append(bullet("Freight Forwarder Liability"))
content.append(bullet("Warehouse Keepers"))

content.append(h3("Engineering Insurance (6 Products)"))
content.append(bullet("Contractors All Risk (CAR)"))
content.append(bullet("Erection All Risk (EAR)"))
content.append(bullet("Contractors Plant and Machinery"))
content.append(bullet("Electronic Equipment"))
content.append(bullet("Machinery Breakdown"))
content.append(bullet("Professional Indemnity"))

content.append(h3("Property Insurance (6 Products)"))
content.append(bullet("Fire and Allied Perils"))
content.append(bullet("Burglary and Theft"))
content.append(bullet("All Risk Property"))
content.append(bullet("Business Interruption"))
content.append(bullet("Plate Glass"))
content.append(bullet("Money Insurance"))
content.append(pagebreak())

content.append(h1("Appendix B: Document Templates"))
content.append(hr())
content.append(table(
    ["LOB", "Document", "Format", "Languages"],
    [
        ["All", "Policy Schedule", "PDF", "Arabic + English"],
        ["All", "Policy Wording", "PDF", "Arabic + English"],
        ["All", "Payment Receipt", "PDF", "Arabic + English"],
        ["All", "Renewal Notice", "PDF", "Arabic"],
        ["Motor", "Insurance Certificate", "PDF + QR", "Bilingual"],
        ["Motor", "Najm Certificate", "PDF", "Bilingual"],
        ["Health", "Member ID Card", "PDF/Digital", "Bilingual"],
        ["Health", "Network Directory", "PDF", "Arabic"],
        ["Travel", "Travel Certificate", "PDF", "English"],
        ["Marine", "Marine Certificate", "PDF", "English"],
        ["Claims", "Claim Acknowledgment", "PDF", "Arabic"],
        ["Claims", "Settlement Letter", "PDF", "Arabic"],
    ],
    col_widths=[1500, 3500, 2000, 2500]
))
content.append(pagebreak())

content.append(h1("Appendix C: API Specifications"))
content.append(hr())
content.append(h2("External API Integrations"))
content.append(table(
    ["API", "Endpoint", "Response Time"],
    [
        ["Elm Vehicle Lookup", "POST /vehicle/lookup", "Less than 500ms"],
        ["Najm Registration", "POST /policy/register", "Less than 1 second"],
        ["Yakeen Verification", "POST /citizen/verify", "Less than 500ms"],
        ["SADAD Payment", "POST /bill/create", "Less than 2 seconds"],
        ["WhatsApp Send", "POST /messages", "Less than 500ms"],
    ],
    col_widths=[3000, 3500, 3000]
))
content.append(h2("Internal API Endpoints"))
content.append(table(
    ["Service", "Endpoint", "Method"],
    [
        ["Quote Engine", "/api/v1/quote", "POST"],
        ["Policy Create", "/api/v1/policy", "POST"],
        ["Document Upload", "/api/v1/documents", "POST"],
        ["OCR Process", "/api/v1/ocr/extract", "POST"],
        ["PDF Generate", "/api/v1/pdf/generate", "POST"],
        ["Fraud Score", "/api/v1/fraud/score", "POST"],
    ],
    col_widths=[3000, 3500, 3000]
))

content.append(hr())
content.append(p("--- End of Document ---", center=True, italic=True, color="666666"))
content.append(p("For questions or next steps, please contact your account representative.", center=True, color="666666"))

# Create document
doc_content = ''.join(content)

DOCUMENT = f'''<?xml version="1.0" encoding="UTF-8" standalone="yes"?>
<w:document xmlns:w="http://schemas.openxmlformats.org/wordprocessingml/2006/main">
<w:body>
{doc_content}
<w:sectPr>
<w:pgSz w:w="12240" w:h="15840"/>
<w:pgMar w:top="1000" w:right="1000" w:bottom="1000" w:left="1000"/>
</w:sectPr>
</w:body>
</w:document>'''

output_path = "/Users/adarshahebbar/Documents/saathi/WhitePaper_Continuation_8to14.docx"

with zipfile.ZipFile(output_path, 'w', zipfile.ZIP_DEFLATED) as zf:
    zf.writestr('[Content_Types].xml', CONTENT_TYPES)
    zf.writestr('_rels/.rels', RELS)
    zf.writestr('word/_rels/document.xml.rels', WORD_RELS)
    zf.writestr('word/document.xml', DOCUMENT)
    zf.writestr('word/styles.xml', STYLES)

print(f"Created: {output_path}")
print("Open this file and copy all content into WhitePaper 2 after Section 7")
