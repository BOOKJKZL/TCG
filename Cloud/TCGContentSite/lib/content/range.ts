import { ApiError } from "./api-error.ts";

export type ByteRange = {
  offset: number;
  length: number;
  end: number;
};

export function parseOpenEndedRange(value: string | null, totalBytes: number): ByteRange | null {
  if (value === null) return null;

  const match = /^bytes=(\d+)-$/.exec(value.trim());
  if (!match) {
    throw new ApiError(416, "只支持单一的开放式 Range：bytes=<offset>-。");
  }
  const offset = Number(match[1]);
  if (!Number.isSafeInteger(offset) || offset < 0 || offset >= totalBytes) {
    throw new ApiError(416, "Range 起点超出内容长度。");
  }

  return {
    offset,
    length: totalBytes - offset,
    end: totalBytes - 1,
  };
}
