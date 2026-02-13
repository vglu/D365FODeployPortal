-- Migration: Add Deployment Archive Support
-- Date: 2026-02-13
-- Description: Adds soft delete (archive) functionality to Deployments table

-- Add IsArchived column (default false)
ALTER TABLE Deployments ADD COLUMN IsArchived INTEGER NOT NULL DEFAULT 0;

-- Add ArchivedAt column (nullable)
ALTER TABLE Deployments ADD COLUMN ArchivedAt TEXT NULL;

-- Create index for better query performance on archived/active deployments
CREATE INDEX IX_Deployments_IsArchived ON Deployments(IsArchived);
