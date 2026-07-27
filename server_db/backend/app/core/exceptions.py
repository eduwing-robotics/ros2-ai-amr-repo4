# 가장 기본이 되는 부모 예외 클래스
class APIException(Exception):
    def __init__(self, error_code: str, message: str, status_code: int = 400):
        self.error_code = error_code
        self.message = message
        self.status_code = status_code

class NotFoundException(APIException):
    def __init__(self, message: str = "요청한 리소스를 찾을 수 없습니다."):
        # 상태 코드를 404로 고정
        super().__init__(error_code="NOT_FOUND", message=message, status_code=404)

class BadRequestException(APIException):
    def __init__(self, message: str = "잘못된 요청입니다.", error_code: str = "BAD_REQUEST"):
        # 상태 코드를 400으로 고정
        super().__init__(error_code=error_code, message=message, status_code=400)

class PermissionDeniedException(APIException):
    def __init__(self, message: str = "권한이 없습니다."):
        # 상태 코드를 403으로 고정
        super().__init__(error_code="PERMISSION_DENIED", message=message, status_code=403)