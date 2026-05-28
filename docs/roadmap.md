# Project Roadmap

## Phase 1: C Sharp and Object-Oriented Programming

Build a console-based ticket tracking application to practice:

- Classes
- Objects
- Properties
- Methods
- Constructors
- Lists
- Basic service logic
- Clean project structure

## Phase 2: Structured Query Language and Relational Database Design

Create database scripts for:

- Departments
- Employees
- Tickets
- Ticket statuses
- Table relationships
- Select queries
- Update queries
- Join queries

## Phase 3: Dataverse Concepts

Translate the database design into Dataverse-style concepts:

- Tables
- Columns
- Choices
- Lookups
- Relationships
- Security roles

## Phase 4: Power Pages Migration

Document how a legacy ticket workflow could move into Power Pages.

## Phase 5: Blazor Web Application

Rebuild part of the console app as a Blazor web application.

## Phase 6: Modernization Notes

Document legacy .NET Framework to modern .NET migration concepts.
"@ | Set-Content docs\roadmap.md

git add docs\roadmap.md
git commit -m "Add project roadmap"
git push
git status
