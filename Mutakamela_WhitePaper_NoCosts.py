#!/usr/bin/env python3
"""
Mutakamela AI Insurance Platform - White Paper Proposal
Version without costs, investment, or implementation timelines
"""

import zipfile
from datetime import datetime

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

# COVER PAGE
content.append(p("", size=22))
content.append(p("WHITE PAPER", bold=True, size=28, center=True, color="666666"))
content.append(p("", size=22))
content.append(p("AI-Enabled Insurance", bold=True, size=48, center=True, color="1F4E79"))
content.append(p("Management Information System", bold=True, size=48, center=True, color="1F4E79"))
content.append(hr())
content.append(p("Transforming 44 Products into One Intelligent Conversation", italic=True, size=26, center=True, color="2E74B5"))
content.append(p("", size=22))
content.append(p("Prepared for", size=20, center=True, color="666666"))
content.append(p("Mutakamela Insurance Company", bold=True, size=28, center=True, color="1F4E79"))
content.append(p("", size=22))
content.append(p(f"Version 1.0 | {datetime.now().strftime('%B %Y')}", size=20, center=True, color="666666"))
content.append(pagebreak())

# TABLE OF CONTENTS
content.append(h1("Table of Contents"))
content.append(hr())
content.append(p("PART I: BUSINESS CASE", bold=True, color="1F4E79"))
content.append(p("    1. Executive Summary"))
content.append(p("    2. The Challenge"))
content.append(p("    3. The Opportunity"))
content.append(p("    4. The Solution"))
content.append(p("PART II: PLATFORM CAPABILITIES", bold=True, color="1F4E79"))
content.append(p("    5. Generic AI Policy Engine"))
content.append(p("    6. WhatsApp-First Experience"))
content.append(p("    7. Document AI"))
content.append(p("    8. Fraud Detection"))
content.append(p("    9. Predictive Analytics"))
content.append(p("PART III: IMPLEMENTATION", bold=True, color="1F4E79"))
content.append(p("    10. Customer Journeys"))
content.append(p("    11. Technical Architecture"))
content.append(p("    12. Integration and Compliance"))
content.append(p("APPENDICES", bold=True, color="1F4E79"))
content.append(p("    A. Complete LOB Coverage (44 Products)"))
content.append(p("    B. Document Templates"))
content.append(p("    C. API Specifications"))
content.append(pagebreak())

# PART I DIVIDER
content.append(p("PART I", bold=True, size=36, center=True, color="1F4E79"))
content.append(p("BUSINESS CASE", bold=True, size=44, center=True, color="2E74B5"))
content.append(pagebreak())

# 1. EXECUTIVE SUMMARY
content.append(h1("1. Executive Summary"))
content.append(hr())
content.append(p("We propose an AI-Enabled Insurance MIS that transforms how Mutakamela serves customers. This platform consolidates 44 insurance products across 7 Lines of Business into a single, intelligent conversation interface."))
content.append(h2("1.1 Key Outcomes"))
content.append(table(
    ["Metric", "Current State", "With AI Platform"],
    [
        ["Time to Quote", "2-4 hours", "Less than 3 minutes"],
        ["Quote to Policy", "1-3 days", "Less than 5 minutes"],
        ["Customer Touchpoints", "5-8 interactions", "1 conversation"],
        ["24/7 Availability", "No", "Yes (AI never sleeps)"],
        ["Fraud Detection", "Manual (days)", "Real-time (seconds)"],
        ["Document Processing", "Manual review", "Automated OCR"],
    ],
    col_widths=[3000, 3200, 3300]
))
content.append(h2("1.2 Strategic Alignment"))
content.append(bullet("Vision 2030: Digital transformation of financial services"))
content.append(bullet("SAMA Regulations: Automated compliance and reporting"))
content.append(bullet("Customer Expectations: Instant, mobile-first, Arabic-native service"))
content.append(bullet("Competitive Position: First-mover advantage in AI-powered insurance"))
content.append(pagebreak())

# 2. THE CHALLENGE
content.append(h1("2. The Challenge"))
content.append(hr())
content.append(h2("2.1 Customer Pain Points"))
content.append(table(
    ["Pain Point", "Current Reality", "Impact"],
    [
        ["Too Many Products", "44 products across 7 LOBs", "Customer confusion"],
        ["Slow Process", "Days for quotes", "Lost sales"],
        ["Channel Friction", "Branch visits required", "Abandonment"],
        ["Language Barriers", "English-first systems", "Frustration"],
        ["No Transparency", "Black-box pricing", "Distrust"],
    ],
    col_widths=[2500, 3500, 3500]
))
content.append(h2("2.2 Operational Challenges"))
content.append(table(
    ["Area", "Issue"],
    [
        ["Manual Data Entry", "Same information entered 5+ times across systems"],
        ["Document Processing", "Human review required for every application"],
        ["Customer Service", "Call center handles basic queries manually"],
        ["Fraud Detection", "Only detected post-claim, not at application"],
        ["Compliance", "Manual report generation prone to errors"],
    ],
    col_widths=[3000, 6500]
))
content.append(pagebreak())

# 3. THE OPPORTUNITY
content.append(h1("3. The Opportunity"))
content.append(hr())
content.append(h2("3.1 Vision 2030 Alignment"))
content.append(table(
    ["Vision 2030 Goal", "How AI Platform Delivers"],
    [
        ["Digital Services", "100% digital policy issuance"],
        ["Financial Sector Development", "Advanced products for all Saudis"],
        ["Quality of Life", "Instant coverage"],
        ["Youth Empowerment", "Digital-native experience"],
    ],
    col_widths=[3500, 6000]
))
content.append(h2("3.2 Market Opportunity"))
content.append(table(
    ["Factor", "Data Point", "Implication"],
    [
        ["WhatsApp Penetration", "95%+ in Saudi", "Preferred channel"],
        ["Smartphone Adoption", "98% of adults", "Mobile-first"],
        ["Youth Population", "70% under 35", "Digital expectations"],
        ["Insurance Penetration", "2% of GDP", "Growth potential"],
    ],
    col_widths=[3000, 3000, 3500]
))
content.append(h2("3.3 Competitive Landscape"))
content.append(table(
    ["Competitor", "WhatsApp Capability", "Gap"],
    [
        ["Tawuniya (29%)", "Planning stage", "No live service"],
        ["Bupa Arabia (25%)", "App-only chat", "Not on WhatsApp"],
        ["Al Rajhi Takaful", "Basic inquiry", "No purchase flow"],
        ["Others", "None", "Phone/email only"],
    ],
    col_widths=[3000, 3000, 3500]
))
content.append(p("Mutakamela can be FIRST to offer full AI-powered quote-to-policy on WhatsApp.", bold=True, color="538135"))
content.append(pagebreak())

# 4. THE SOLUTION
content.append(h1("4. The Solution"))
content.append(hr())
content.append(h2("4.1 Core Concept"))
content.append(p("One intelligent conversation that understands what the customer needs and delivers the right product instantly."))
content.append(numbered("Listens to natural language request", 1))
content.append(numbered("Understands intent and extracts information", 2))
content.append(numbered("Matches to optimal product(s)", 3))
content.append(numbered("Asks only needed questions", 4))
content.append(numbered("Generates instant quote", 5))
content.append(numbered("Processes payment securely", 6))
content.append(numbered("Delivers policy PDF in same conversation", 7))
content.append(h2("4.2 Platform Components"))
content.append(table(
    ["Component", "Function", "Technology"],
    [
        ["AI Policy Engine", "Intent matching", "Arabic-BERT, Rasa"],
        ["WhatsApp Integration", "Customer interface", "Meta Business API"],
        ["Document AI", "OCR and verification", "Tesseract, LayoutLM"],
        ["Fraud Detection", "Risk scoring", "ML anomaly detection"],
        ["PDF Generation", "Instant documents", "Jinja2, WeasyPrint"],
    ],
    col_widths=[2800, 3500, 3200]
))
content.append(h2("4.3 Lines of Business"))
content.append(table(
    ["LOB", "Products", "AI Capabilities"],
    [
        ["Motor", "7 products", "Instant quotes, Najm integration"],
        ["Health", "8 products", "Family enrollment, network lookup"],
        ["Travel", "5 products", "Instant certificates"],
        ["Protection", "6 products", "Needs analysis"],
        ["Marine", "6 products", "Open policy management"],
        ["Engineering", "6 products", "Project coverage"],
        ["Property", "6 products", "Risk assessment"],
    ],
    col_widths=[1800, 2500, 5200]
))
content.append(p("Total: 44 products unified into ONE intelligent interface", bold=True, center=True, color="1F4E79"))
content.append(pagebreak())

# PART II DIVIDER
content.append(p("PART II", bold=True, size=36, center=True, color="1F4E79"))
content.append(p("PLATFORM CAPABILITIES", bold=True, size=44, center=True, color="2E74B5"))
content.append(pagebreak())

# 5. AI POLICY ENGINE
content.append(h1("5. Generic AI Policy Engine"))
content.append(hr())
content.append(h2("5.1 How It Works"))
content.append(table(
    ["Stage", "Process", "Example"],
    [
        ["1. Intent Classification", "Detect BUY, RENEW, CLAIM", "I need insurance -> BUY"],
        ["2. Entity Extraction", "Extract Product, Amount, Dates", "my Camry -> Vehicle"],
        ["3. Needs Analysis", "Understand requirements", "new car -> Comprehensive"],
        ["4. Product Matching", "Score suitable products", "Motor Comprehensive 95%"],
        ["5. Gap Filling", "Ask missing fields", "Plate number?"],
        ["6. Quote Generation", "Calculate premium", "Quote generated instantly"],
    ],
    col_widths=[2500, 3500, 3500]
))
content.append(h2("5.2 Policy Matching Algorithm"))
content.append(p("MATCH SCORE = (Keyword Match x 0.3) + (Entity Match x 0.3) + (Need Alignment x 0.4)", bold=True, center=True, color="2E74B5"))
content.append(h2("5.3 Arabic Language Support"))
content.append(bullet("Arabic-BERT: Pre-trained on Arabic insurance corpus"))
content.append(bullet("Saudi Dialect: Understands local expressions"))
content.append(bullet("Code-Switching: Handles mixed Arabic-English"))
content.append(bullet("Context Awareness: Understands Gulf Arabic nuances"))
content.append(pagebreak())

# 6. WHATSAPP
content.append(h1("6. WhatsApp-First Customer Experience"))
content.append(hr())
content.append(h2("6.1 Customer Entry Points"))
content.append(table(
    ["Entry Point", "How It Works"],
    [
        ["Website Widget", "Floating WhatsApp button on mutakamela.com"],
        ["QR Code", "Scan at branches, billboards, brochures"],
        ["Click-to-WhatsApp Ads", "Facebook/Instagram ads open chat directly"],
        ["SMS Campaigns", "Renewal reminder with wa.me link"],
        ["Policy Documents", "WhatsApp number printed on every PDF"],
    ],
    col_widths=[3000, 6500]
))
content.append(h2("6.2 Deep Link Technology"))
content.append(p("https://wa.me/966512345678?text=I need insurance", italic=True, center=True, color="2E74B5"))
content.append(bullet("No need to save contact - link opens chat directly"))
content.append(bullet("Pre-filled message reduces customer effort"))
content.append(bullet("Works on iOS, Android, WhatsApp Web"))
content.append(h2("6.3 Customer Authentication"))
content.append(table(
    ["Scenario", "Method", "Flow"],
    [
        ["Existing Customer", "Phone matches CRM", "Greet by name"],
        ["New Customer", "Collect Iqama/ID", "Verify via Yakeen"],
        ["Sensitive Actions", "OTP verification", "6-digit code"],
        ["Payment", "Secure redirect", "HTTPS payment page"],
    ],
    col_widths=[2500, 3000, 4000]
))
content.append(pagebreak())

# 7. DOCUMENT AI
content.append(h1("7. Intelligent Document Processing"))
content.append(hr())
content.append(h2("7.1 Document Types Supported"))
content.append(table(
    ["Document", "Fields Extracted", "Accuracy"],
    [
        ["National ID (Iqama)", "Name, ID, DOB, Nationality", "99.5%"],
        ["Vehicle Registration", "Plate, VIN, Make, Model", "99%"],
        ["Driving License", "License#, Category, Expiry", "99%"],
        ["Medical Reports", "Diagnosis, Treatment", "95%"],
        ["Commercial Register", "CR#, Company, Activities", "98%"],
    ],
    col_widths=[3000, 4500, 2000]
))
content.append(h2("7.2 Arabic OCR Capabilities"))
content.append(bullet("Bidirectional text: Arabic RTL + English LTR"))
content.append(bullet("Handwritten Arabic recognition"))
content.append(bullet("Government document QR codes"))
content.append(bullet("Stamp and seal detection"))
content.append(h2("7.3 Verification Checks"))
content.append(table(
    ["Check", "Method", "Action"],
    [
        ["Authenticity", "Tamper detection", "Flag altered docs"],
        ["Validity", "Expiry + Absher check", "Reject expired"],
        ["Consistency", "Cross-field validation", "Flag mismatches"],
        ["Quality", "Image scoring", "Request clearer scan"],
    ],
    col_widths=[2500, 3500, 3500]
))
content.append(p("Total processing time: less than 3 seconds", bold=True, color="538135"))
content.append(pagebreak())

# 8. FRAUD DETECTION
content.append(h1("8. Fraud Detection and Risk Assessment"))
content.append(hr())
content.append(h2("8.1 Real-Time Fraud Detection"))
content.append(table(
    ["Detection Type", "Technique", "Red Flags"],
    [
        ["Application Fraud", "NLP + Anomaly", "Inconsistent answers"],
        ["Document Fraud", "Computer Vision", "Photoshopped docs"],
        ["Claims Fraud", "Pattern Recognition", "Staged accidents"],
        ["Identity Fraud", "Biometric Check", "Stolen IDs"],
        ["Collusion", "Network Analysis", "Related parties"],
    ],
    col_widths=[2500, 3000, 4000]
))
content.append(h2("8.2 Fraud Risk Score"))
content.append(table(
    ["Score", "Risk Level", "Action"],
    [
        ["0-30", "Low", "Auto-approve"],
        ["31-60", "Medium", "Enhanced verification"],
        ["61-80", "High", "Manual review"],
        ["81-100", "Critical", "Block and escalate"],
    ],
    col_widths=[1500, 2500, 5500]
))
content.append(h2("8.3 Underwriting Engine"))
content.append(table(
    ["Decision", "Criteria", "Response Time"],
    [
        ["Auto-Accept", "Standard risk, complete data", "Less than 5 seconds"],
        ["Auto-Quote", "Moderate risk", "Less than 30 seconds"],
        ["Refer", "High-value, complex", "Queued for review"],
        ["Decline", "Uninsurable, fraud flags", "Instant"],
    ],
    col_widths=[2500, 4500, 2500]
))
content.append(pagebreak())

# 9. ANALYTICS
content.append(h1("9. Predictive Analytics"))
content.append(hr())
content.append(h2("9.1 Claims Prediction"))
content.append(table(
    ["Prediction", "Model", "Business Value"],
    [
        ["Claim Frequency", "Time-series", "Reserve planning"],
        ["Claim Severity", "Regression", "Loss ratio prediction"],
        ["Settlement Amount", "XGBoost", "Early reserve setting"],
        ["Litigation Risk", "NLP", "Legal intervention"],
    ],
    col_widths=[2500, 3000, 4000]
))
content.append(h2("9.2 Customer Analytics"))
content.append(table(
    ["Metric", "Capability", "Action"],
    [
        ["Churn Prediction", "Behavioral analysis", "Retention offers"],
        ["Cross-sell", "Affinity modeling", "Recommendations"],
        ["Lifetime Value", "Revenue modeling", "Tiered service"],
        ["Renewal Probability", "Historical patterns", "Campaign timing"],
    ],
    col_widths=[2800, 3200, 3500]
))
content.append(h2("9.3 Operational Dashboard"))
content.append(bullet("Real-time policy issuance metrics"))
content.append(bullet("Claims processing funnel"))
content.append(bullet("Agent performance scorecards"))
content.append(bullet("Channel effectiveness comparison"))
content.append(bullet("Revenue and loss ratio trending"))
content.append(pagebreak())

# PART III DIVIDER
content.append(p("PART III", bold=True, size=36, center=True, color="1F4E79"))
content.append(p("IMPLEMENTATION", bold=True, size=44, center=True, color="2E74B5"))
content.append(pagebreak())

# 10. CUSTOMER JOURNEYS
content.append(h1("10. End-to-End Customer Journeys"))
content.append(hr())
content.append(h2("10.1 Motor Insurance Journey"))
content.append(p("Customer: I need car insurance for my new Camry"))
content.append(table(
    ["Step", "Action", "System"],
    [
        ["1", "Customer clicks WhatsApp button on website", "Entry"],
        ["2", "AI asks: Comprehensive or Third Party?", "AI Engine"],
        ["3", "Customer: Comprehensive with agency repair", "Chat"],
        ["4", "AI: Plate number please?", "AI Engine"],
        ["5", "AI fetches vehicle details from Elm", "API"],
        ["6", "AI shows quote and asks to proceed", "Quote"],
        ["7", "Customer confirms, payment completed", "Gateway"],
        ["8", "Customer uploads Istimara, OCR processes", "Document AI"],
        ["9", "Policy PDF sent via WhatsApp", "Delivery"],
    ],
    col_widths=[1000, 6000, 2500]
))
content.append(p("Total: Under 3 minutes from first message to policy in hand", bold=True, color="538135", center=True))
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
        ["Insurance Certificate", "PDF with QR", "WhatsApp"],
        ["Payment Receipt", "PDF", "WhatsApp + Email"],
    ],
    col_widths=[3000, 3000, 3500]
))
content.append(pagebreak())

# 11. TECHNICAL ARCHITECTURE
content.append(h1("11. Technical Architecture"))
content.append(hr())
content.append(h2("11.1 Technology Stack"))
content.append(table(
    ["Layer", "Technology", "Purpose"],
    [
        ["AI/NLP", "Arabic-BERT, Rasa", "Intent, Entity, Dialogue"],
        ["ML Platform", "TensorFlow, PyTorch", "Fraud, Risk models"],
        ["Backend", "Python FastAPI", "APIs, Business Logic"],
        ["Database", "PostgreSQL + Redis", "Data + Cache"],
        ["Document AI", "Tesseract + LayoutLM", "OCR extraction"],
        ["PDF Engine", "Jinja2 + WeasyPrint", "Template rendering"],
        ["Cloud", "AWS Bahrain", "Saudi data residency"],
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
        ["End-to-End", "Less than 5 minutes", "95%"],
    ],
    col_widths=[3500, 3000, 3000]
))
content.append(h2("11.3 Security"))
content.append(bullet("Data Encryption: AES-256 at rest, TLS 1.3 in transit"))
content.append(bullet("Saudi Data Residency: AWS Bahrain region"))
content.append(bullet("SAMA Compliance: Automated reporting"))
content.append(bullet("PCI-DSS: Payment data isolation"))
content.append(pagebreak())

# 12. INTEGRATION
content.append(h1("12. Integration and Compliance"))
content.append(hr())
content.append(h2("12.1 External Integrations"))
content.append(table(
    ["System", "Purpose", "Data Exchange"],
    [
        ["Elm", "Vehicle lookup", "Plate to Make/Model"],
        ["Najm", "Motor registry", "Policy registration"],
        ["Yakeen", "Citizen verification", "ID to Name/DOB"],
        ["Absher", "ID validation", "Authenticity check"],
        ["SADAD", "Bill payment", "Premium collection"],
        ["SAMA", "Regulatory", "Compliance reports"],
    ],
    col_widths=[2200, 3300, 4000]
))
content.append(h2("12.2 SAMA Compliance"))
content.append(table(
    ["Requirement", "Automation", "Output"],
    [
        ["Policy Documentation", "Auto-generated", "Compliant wordings"],
        ["Premium Calculations", "Tariff validation", "Audit trail"],
        ["Claims Reporting", "Auto-classification", "Regulatory reports"],
        ["AML/KYC", "ID verification", "Compliance certificates"],
    ],
    col_widths=[2800, 3200, 3500]
))
content.append(h2("12.3 Audit and Transparency"))
content.append(bullet("Complete logging of all AI decisions"))
content.append(bullet("Explainable AI for underwriting"))
content.append(bullet("Document version control (7 years)"))
content.append(bullet("Data lineage tracking"))
content.append(pagebreak())

# APPENDIX A
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

# APPENDIX B
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

# APPENDIX C
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

output_path = "/Users/adarshahebbar/Documents/saathi/Mutakamela_AI_WhitePaper_Final.docx"

with zipfile.ZipFile(output_path, 'w', zipfile.ZIP_DEFLATED) as zf:
    zf.writestr('[Content_Types].xml', CONTENT_TYPES)
    zf.writestr('_rels/.rels', RELS)
    zf.writestr('word/_rels/document.xml.rels', WORD_RELS)
    zf.writestr('word/document.xml', DOCUMENT)
    zf.writestr('word/styles.xml', STYLES)

print(f"Created: {output_path}")
