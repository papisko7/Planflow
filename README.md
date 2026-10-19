# PlanFlow 🚀
> **Mobile system supporting group time organization and event planning**  
> *Mobilny system wspierający grupową organizację czasu i planowanie wydarzeń*

---

## 🔒 Copyright & Licensing

**All Rights Reserved © 2026 Kacper Kowalski**

This source code, architecture design, and documentation form an integral part of an engineering thesis conducted at the University of Zielona Góra.  
**Copying, distributing, modifying, or using this codebase** (for both commercial and non-commercial purposes) without explicit, written permission from the author is strictly prohibited.

---

## 🎓 Academic Thesis Details

* **University:** University of Zielona Góra (*Uniwersytet Zielonogórski*)
* **Faculty:** *Wydział Nauk Inżynieryjno-Technicznych*
* **Major:** Computer Science (*Informatyka*)
* **Specialization:** Microinformatics Systems Engineering (*Inżynieria Systemów Mikroinformatycznych*)
* **Level & Mode:** First-cycle studies, full-time
* **Author:** Kacper Kowalski (Student ID: 112090)
* **Thesis Supervisor:** Tomasz Gratkowski, Ph.D., Eng.
* **Project Start Date:** March 24, 2026

---

## 🎯 Project Goal

The primary objective of this project is to design and implement the **PlanFlow** mobile system, engineered to automate routine tasks and schedule management within groups (e.g., households). The system reduces decision paralysis with a deterministic, explainable **Urgency Score** algorithm (no AI), team availability and workload hints, and read-only integration with Google Calendar and Google Tasks.

---

## 📋 Scope of Work

1. 📚 **Literature Review** on mobile time organization and decision-support systems.
2. 🔍 **Problem Identification** and analysis of decision paralysis in group settings.
3. 🧠 **Decision Model Development** (Urgency Score v2) to automate prioritization in group planning.
4. 🏗️ **System Architecture Design** for a layered mobile system.
5. ⚙️ **Backend API Implementation** (business logic, background jobs, recalculations).
6. 📅 **Integration** with the Google Calendar API (OAuth 2.0 with PKCE, import on demand).
7. 📱 **Mobile Application & Dashboard Implementation** (React Native).
8. 🧪 **System Validation**: unit, integration, end-to-end and performance tests.

---

## 🛠️ Repository Structure

* `backend/` – .NET 10 solution. `src/PlanFlow.Domain` (entities, pure rules such as `UrgencyScoreCalculator`), `src/PlanFlow.Application` (MediatR commands and queries), `src/PlanFlow.Infrastructure` (EF Core, Redis, Quartz jobs, Google/SMTP/Expo clients), `src/PlanFlow.Api` (controllers, SignalR chat hub); `tests/PlanFlow.Tests` (xUnit, Testcontainers PostgreSQL); `benchmarks/PlanFlow.Benchmarks` (NBomber load tests).
* `mobile/` – React Native (Expo, TypeScript) app; unit tests with Jest and React Testing Library; end-to-end scenario in `.maestro/`.
* `documents/diagrams/` – Mermaid diagrams: ERD (21 tables), C4 container diagram, Urgency Score activity diagram (algorithm v2).
* `scripts/` – `start-demo.cmd` / `start-demo.ps1`: one-command demo startup (containers, backend, tunnels, Expo).

## ▶️ Running and Testing

* Demo: `scripts\start-demo.cmd` (checks tools, starts PostgreSQL/Redis/Mailpit containers, applies migrations, starts the backend and Expo).
* Backend tests (Docker required for the PostgreSQL/Redis tests): `dotnet test backend/tests/PlanFlow.Tests/PlanFlow.Tests.csproj`.
* Mobile tests: `cd mobile && npx jest` and `npx tsc --noEmit`.
* Secrets (JWT key, Google OAuth credentials, SMTP) belong in `dotnet user-secrets` or environment variables; the committed `appsettings.json` holds development defaults only.

## ✅ Status

Implementation complete (all code-related thesis scope points delivered). Backend: 1282 tests; mobile: 563 tests; all passing. Documentation and the thesis knowledge base are kept outside the repository (Obsidian vault).
