#!/usr/bin/env bash
set -e

echo "========================================="
echo "  Manfoods McDonald's - Post-merge setup"
echo "========================================="

echo ""
echo "Pushing database schema..."
make db-push

echo ""
echo "✅ Post-merge setup complete!"
