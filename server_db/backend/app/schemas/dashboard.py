from pydantic import BaseModel
from typing import Dict

class DashboardSummaryResponse(BaseModel):
    ok: bool
    today_summary: Dict[str, Dict[str, int]]

    class Config:
        json_schema_extra = {
            "example": {
                "ok": True,
                "today_summary": {
                    "attendance": {"current_in": 0, "current_out": 0},
                    "visitor": {"today_total": 0},
                    "violation": {"NO_HELMET": 0},
                    "emergency": {"FALL": 0, "FIRE": 0}
                }
            }
        }