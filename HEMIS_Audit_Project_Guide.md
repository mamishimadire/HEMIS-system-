# HEMIS Audit System — Project Guide

**Prepared by:** Mamishi Madire  
**Organisation:** SNG Grant Thornton  
**Document Type:** Project Overview & User Guide  
**Date:** June 2026

---

## Table of Contents

1. [What Is HEMIS and Why Does It Need Auditing?](#1-what-is-hemis-and-why-does-it-need-auditing)
2. [Why I Built This System](#2-why-i-built-this-system)
3. [What the System Does](#3-what-the-system-does)
4. [How the System Assists SNG Grant Thornton](#4-how-the-system-assists-sng-grant-thornton)
5. [The 67 Audit Rules](#5-the-67-audit-rules)
6. [Live Reference Tools Built Into the System](#6-live-reference-tools-built-into-the-system)
7. [Roles and Access Control](#7-roles-and-access-control)
8. [The Signoff Workflow](#8-the-signoff-workflow)
9. [Technology Overview](#9-technology-overview)
10. [Summary](#10-summary)

---

## 1. What Is HEMIS and Why Does It Need Auditing?

**HEMIS** (Higher Education Management Information System) is the national data system used by South African higher education institutions — universities, universities of technology, and private higher education institutions — to submit student and staff data to the Department of Higher Education and Training (DHET). This data is used to allocate government funding to institutions.

Because public funding is determined directly by the data submitted through HEMIS, the accuracy and integrity of that data is critical. Errors, inconsistencies, or irregularities in the submitted data can result in:

- Incorrect government subsidy allocations
- Funding being paid for students who do not qualify
- Non-compliance with HEMIS directives and NQF requirements
- Reputational and regulatory risk for the institution

SNG Grant Thornton is engaged by higher education institutions to independently audit their HEMIS data before it is submitted to DHET, verifying that the data complies with the HEMIS Data Element Dictionary (DEETYAPAC), audit directives, and applicable NQF standards.

---

## 2. Why I Built This System

### The Problem with Scripts

Before this system existed, HEMIS audits were performed using technical scripts — SQL queries, Python programs, R scripts, or Jupyter Notebooks. While these tools are powerful, they present a significant barrier in an audit environment:

- A team member must understand how to **read and interpret code** to follow the logic of a validation
- A trainee auditor must be **taught to write or modify scripts** before they can contribute meaningfully
- When an auditor leaves the team, the institutional knowledge embedded in their scripts leaves with them
- Scripts are hard to **standardise** — every auditor may write the same check differently
- There is no built-in **workflow** for review, approval, or sign-off; these steps are managed manually

This creates a situation where the quality and speed of the audit depends heavily on the technical skill of the individual performing it, which is inconsistent with how a professional audit firm manages risk and quality.

### A Better Approach: A Purpose-Built Audit Tool

Consider how SNG Grant Thornton's auditors use **LEAP** — the firm's audit management platform. Trainees, seniors, managers, and directors can all use LEAP effectively after training, regardless of their technical background. Nobody needs to know how LEAP's database is structured or how its queries are written. The tool presents a clear interface, guides the user through the process, and enforces the workflow.

**I built this system for exactly the same reason.**

The HEMIS Audit System is a purpose-built, browser-based application that brings the same philosophy to HEMIS data auditing:

- Any team member can be **trained to operate the system** within a short period
- The audit logic is **built into the system** — users do not need to understand SQL or statistics to run a validation
- All 67 audit rules are presented through a **consistent, guided interface**
- The workflow — from running a validation to analyst sign-off, manager review, and director approval — is **enforced by the system**
- Results are **stored in a database** and can be retrieved at any time, just as archived engagements are accessible in LEAP

This shifts HEMIS auditing from a technical, individual-dependent activity into a **managed, repeatable, team-based process** consistent with professional audit standards.

---

## 3. What the System Does

The HEMIS Audit System is a full web-based audit management platform. The following is a summary of its capabilities.

### 3.1 Engagement Management

Every HEMIS audit is managed as an **engagement** (referred to in the system as a client). An engagement captures:

- The name of the institution being audited
- The fiscal/reporting year under audit
- The institution type (University, TVET College, Private Higher Education Institution, etc.)
- The team assigned to the engagement and their roles
- All validation runs performed during the engagement
- The approval and archive status of the engagement

Engagements move through a lifecycle: **Pending → Active → Archived**. Only approved and fully signed-off engagements can be archived.

### 3.2 The 67 Audit Rule Modules

The core of the system is a set of **67 individual audit rule modules**, each corresponding to a specific HEMIS audit validation. Each module:

1. Allows the auditor to **connect to the institution's HEMIS database** on their SQL Server
2. Provides a **dynamic configuration interface** where the auditor maps the relevant HEMIS tables and columns to the rule parameters
3. **Generates and executes the validation query** against the live data
4. Presents results clearly: total records validated, pass count, fail count, exception rate, and sample rows
5. Stores the **full results in the system database** so they can be retrieved and reviewed at any time
6. Supports **multi-level sign-off** (analyst, manager, director)
7. Allows **export** of results to Excel, CSV, or the raw SQL query

### 3.3 Result Storage and Portfolio Dashboard

Every validation run is saved to the system's database. The **dashboard** provides a portfolio-level view of all engagements, showing:

- Which rules have been run for each engagement
- The current pass/fail outcome per rule
- The sign-off status across the team
- Pending approvals requiring attention

This means the full audit history of every engagement is preserved in the system — auditors do not need to track results in separate spreadsheets or files.

### 3.4 Internal Messaging

The system includes an **internal messaging platform** allowing the audit team to communicate within the context of the engagement. Messages support file attachments (up to 15 MB), thread-based conversations, read receipts, and edit/delete functionality.

### 3.5 Audit Trail

Every significant action taken in the system is recorded in an **audit log**: logins, logouts, validation runs, sign-offs, downloads, user management actions, and more. This provides full traceability of who did what and when.

### 3.6 User and Access Management

Administrators can create and manage user accounts, assign users to engagements with appropriate roles, enforce password policies, and monitor account activity. The system uses role-based access control aligned with the firm's engagement hierarchy.

---

## 4. How the System Assists SNG Grant Thornton

### 4.1 Speed and Consistency

Traditional script-based HEMIS validation requires time to write, test, and execute code for each rule. The system has the validation logic for all 67 rules **pre-built and tested**. An auditor can connect to a client's database, configure the rule parameters, and run the validation in minutes — without writing a single line of code.

Because every team member uses the same system, the output of the same rule is identical regardless of who runs it. This eliminates the risk of different auditors interpreting or implementing a rule differently.

### 4.2 A Database of Engagement Records — Like LEAP

One of the most significant benefits of the system is its **persistent database**. Every engagement, every validation run, and every result is stored and retrievable.

This mirrors the way LEAP works for the broader audit: archived engagements remain accessible, historical results can be reviewed, and the firm builds an institutional knowledge base that does not depend on any individual team member's files or scripts.

In practical terms:

- A manager can open the system and immediately see the current state of every active engagement
- A director can review signed-off results without waiting for a file to be emailed
- Historical validation runs from prior years are available for comparison
- Nothing is lost when a team member leaves or rolls off the engagement

### 4.3 Anyone on the Team Can Operate It

The system is designed to be used by the full engagement team — including trainees and junior staff — without technical knowledge:

| Role | What they can do |
|------|-----------------|
| **Trainee** | View results, download exports, follow the engagement progress |
| **Data Analyst** | Configure and run all 67 rule validations, save workspaces, sign off results as the preparer |
| **Manager** | Review analyst-signed results, add manager sign-off, manage team assignments |
| **Director** | Review all results, provide director-level approval, archive completed engagements |
| **Admin** | Full system administration: users, engagements, audit log, system configuration |

A new team member requires only brief onboarding on how to navigate the system — not training in SQL, Python, or statistics.

### 4.4 Enforced Workflow and Sign-Off

The system enforces a structured review workflow that mirrors the firm's quality control requirements:

1. A **Data Analyst** runs the validation and adds an analyst sign-off
2. A **Manager** reviews the analyst's results and adds a manager sign-off
3. A **Director** reviews the completed engagement and provides final approval
4. Only after all sign-offs are in place can the engagement be archived

This workflow is enforced by the system — it is not possible to skip steps or sign off at the wrong level. This provides the same quality assurance structure as the firm's other audit tools.

### 4.5 Export-Ready Evidence

For every validation run, the system can produce:

- **Excel exports** with formatted results sheets, exception details, and metadata
- **CSV exports** for further analysis
- **SQL exports** of the exact query that was executed (useful for documentation and peer review)
- **R script exports** for statistical validation workflows where required

These exports serve as the audit evidence that is filed in the engagement file alongside the sign-off records.

### 4.6 Faster Engagement Turnaround

Because the system eliminates the time spent writing, debugging, and running scripts, and because results are immediately visible and accessible to all team members, the overall turnaround time for a HEMIS engagement is significantly shorter. The team can focus on **analysing exceptions and forming audit conclusions** rather than on the mechanics of data extraction and validation.

---

## 5. The 67 Audit Rules

The system implements **67 audit rules** covering the full scope of a HEMIS data audit. These rules are grouped broadly as follows:

### Rules 1–10: Basic Data Integrity

Checks fundamental data quality in the HEMIS tables — missing fields, duplicate codes, invalid reference values, and referential integrity between the core tables (QUAL, CRSE, STUD, CREG).

Examples:
- **Rule 1**: Qualifications without a qualification type code
- **Rule 3**: Duplicate qualification codes in the QUAL table
- **Rule 5**: Students recorded with placeholder student numbers (e.g. "9999999")
- **Rule 7**: Students linked to qualification codes that do not exist in the QUAL table
- **Rule 9**: Course registrations for student numbers that do not exist in the STUD table

### Rules 11–20: Student and Course Cross-Validation

Validates consistency between student records and course registration data — ensuring that students are registered for courses that exist, at appropriate levels, with valid credit allocations.

### Rules 21–30: Academic Delivery Validation

Verifies that course delivery, credit allocation, and registration status fields comply with HEMIS directives and the Data Element Dictionary.

### Rules 31–40: Student Progression and Status

Checks student progression records, completion statuses, and re-registration flags against expected patterns defined in the HEMIS directives.

### Rules 41–50: Qualification and NQF Alignment

Validates that qualifications offered by the institution are correctly classified against the NQF — correct level, correct sub-framework (HEQSF, OQSF, GENFETQA), and correct qualification type.

### Rules 51–60: Special Populations

Validates data for special population groups including international students, students with disabilities, and equity reporting fields.

### Rules 61–67: Funding, Census, and Submission Validation

Validates data specifically relevant to the DHET subsidy calculation, census date records, and submission-readiness checks.

Each of the 67 rules follows the same interface pattern — users who know how to operate one rule can operate all of them.

---

## 6. Live Reference Tools Built Into the System

### 6.1 DEETYAPAC Help — Live HEMIS Reference

The **DEETYAPAC Help module** provides every auditor with live access to the complete HEMIS Data Element Dictionary and audit directives hosted at `www.heda.co.za/Valpac_Help/`.

This means that when an auditor is configuring a rule or reviewing a result and needs to check what a specific data element means, or what the directive says about a particular field, they do not need to open a separate browser, navigate to an external site, or hunt for a PDF. The reference material is embedded directly in the audit system, always showing the **current live version** from the source.

If DHET or HEDA updates the DEETYAPAC content, the system automatically reflects those changes because it fetches the content live each time. Auditors are always working from the most up-to-date reference.

The DEETYAPAC module includes:

- A full navigation sidebar organised into logical groups (Introduction, Data Elements, Directives, Reference, etc.)
- Direct access to the Base Element Dictionary covering all 110+ HEMIS data elements
- Audit Directives (February 2008 and April 2009)
- CESM codes, credit value tables, edit validation rules, and glossary
- Back/Forward navigation and a Reload button, functioning like a browser within the system

### 6.2 SAQA Search — Live Qualification Verification

For engagements involving clinical training programmes and other regulated qualifications, the system provides **live access to the South African Qualifications Authority (SAQA) qualification register** at `allqs.saqa.org.za`.

This allows auditors to verify, directly from within the system:

- Whether a qualification is currently registered on the NQF
- The qualification's NQF level, sub-framework, and minimum credits
- The originating institution
- Critical registration dates: Registration Start Date, Registration End Date, Last Date for Enrolment, and Last Date for Achievement

#### The SAQA Search Guide

The system includes a built-in **four-step interactive guide** that teaches auditors how to use the SAQA search effectively:

| Step | Content |
|------|---------|
| **Step 1 — Search Form** | How to use each search field (Title, ID, NQF Level, Originator, Word Search) with a visual mockup of the SAQA search screen |
| **Step 2 — Reading Results** | How to interpret the results table (columns, status meanings, how to open a qualification record) |
| **Step 3 — Qualification Detail** | Field-by-field explanation of every item on the SAQA qualification detail page, including SAQA QUAL ID, NQF Sub-framework, Qualification Type, Registration Status, SAQA Decision Number, and all classification codes |
| **Step 4 — Dates and Timeline** | Plain-language explanation of the four critical dates with a visual colour-coded timeline and specific audit implications (what to verify, what the dates mean for student enrolment and certification) |

The guide opens as a panel alongside the live SAQA search, so auditors can read the explanation while simultaneously searching and verifying qualifications — without switching between screens or consulting a separate reference document.

---

## 7. Roles and Access Control

The system uses five roles aligned with the typical SNG Grant Thornton engagement structure:

| Role | Description |
|------|-------------|
| **Admin** | Full system control — create and manage users, engagements, and system settings. View audit log. |
| **Director** | Create and manage engagements, assign users, review and approve results, archive completed engagements. |
| **Manager** | Review Data Analyst results, add manager-level sign-off, manage team assignments. |
| **Data Analyst** | Run all 67 audit rule validations, configure rule parameters, save and restore workspaces, prepare results for review. |
| **Trainee** | View all results and engagement data, download exports — read-only access for learning and observation. |

Users are assigned to specific engagements. A user only has access to the engagements they are assigned to, ensuring data confidentiality between client engagements.

**Security features include:**

- Password policy enforcement (minimum 8 characters, uppercase, lowercase, number, and special character required)
- Password expiry and forced renewal
- Account lockout after 5 failed login attempts
- Password history to prevent reuse
- Admin-forced password reset capability
- Full audit log of all user and system actions

---

## 8. The Signoff Workflow

Every validation run in the system goes through a structured approval process before the engagement can be closed:

```
Data Analyst runs validation
        ↓
Analyst reviews results and adds ANALYST SIGN-OFF
        ↓
Manager reviews analyst-signed results
        ↓
Manager adds MANAGER SIGN-OFF
        ↓
Director reviews all signed results
        ↓
Director APPROVES and ARCHIVES the engagement
```

This workflow is enforced at every step — the system will not allow sign-offs to be applied out of sequence or by the wrong role. If a sign-off is removed (for example, because an exception requires re-investigation), the engagement returns to the appropriate prior state and a new validation run must be completed.

Once an engagement is archived, it becomes read-only. All results and sign-offs are permanently preserved in the system database.

---

## 9. Technology Overview

The system is built on modern, widely supported technology that does not require any proprietary licences beyond what the firm already has:

| Component | Technology |
|-----------|-----------|
| Application framework | ASP.NET Core MVC (C#) |
| Application database | SQLite (engagement data, users, results metadata) |
| HEMIS data connection | Microsoft SQL Server (client's existing HEMIS database) |
| Authentication | ASP.NET Identity with role-based access control |
| Frontend | HTML5, CSS3, Vanilla JavaScript |
| Exports | Excel (.xlsx), CSV, SQL script, R Script |
| Deployment | Windows Server / IIS |

The system connects to the institution's existing SQL Server database — it does not require any changes to the client's HEMIS environment. All validation queries are **read-only**; the system never writes to or modifies the client's database.

---

## 10. Summary

The HEMIS Audit System was built to solve a practical problem: HEMIS data auditing is a specialised, repeatable process that should not depend on the technical scripting skills of individual team members.

By building a purpose-built audit application — rather than relying on SQL scripts, Python programs, or Jupyter notebooks — the system brings the same benefits that LEAP brings to the broader audit practice:

- **Anyone on the team can use it** after brief training, from trainees to directors
- **Results are stored in a database** and are always accessible, regardless of who ran them or when — archived engagements remain available exactly as they do in LEAP
- **The workflow is enforced** — analyst preparation, manager review, and director approval are built into the system and cannot be bypassed
- **Reference tools are built in and live** — auditors have direct access to DEETYAPAC and the SAQA register without leaving the system, and content is always current because it is fetched live from the source
- **Guides are built in** — the system teaches users how to use reference tools effectively, including how to read SAQA qualification records, interpret critical NQF dates, and understand what each field on a qualification detail page means
- **Exports are production-ready** — Excel, CSV, SQL, and R script outputs serve directly as audit evidence and can be filed in the engagement file
- **The audit log provides full traceability** — every action in the system is recorded with the user, timestamp, and IP address, supporting both quality review and regulatory compliance

The result is a professional, scalable, and team-friendly audit tool that makes HEMIS auditing faster, more consistent, and accessible to the full engagement team — aligned with the way SNG Grant Thornton manages quality and risk across all its service lines.

---

*Document prepared by Mamishi Madire | SNG Grant Thornton | June 2026*
