import type { FormEvent } from 'react';
import type { ZodType } from 'zod';

interface SubmitAuthFormOptions<TFormData> {
  event: FormEvent<HTMLFormElement>;
  formData: TFormData;
  schema: ZodType<TFormData>;
  submit: () => Promise<void>;
  setError: (message: string) => void;
  setIsLoading: (isLoading: boolean) => void;
  fallbackMessage: string;
  onSuccess?: () => void | Promise<void>;
  onSettled?: () => void | Promise<void>;
}

function getValidationError<TFormData>(schema: ZodType<TFormData>, formData: TFormData): string | null {
  const result = schema.safeParse(formData);
  if (result.success) {
    return null;
  }

  return result.error.issues[0]?.message ?? 'Invalid form data';
}

function getSubmissionErrorMessage(error: unknown, fallbackMessage: string): string {
  return error instanceof Error ? error.message : fallbackMessage;
}

export async function submitAuthForm<TFormData>({
  event,
  formData,
  schema,
  submit,
  setError,
  setIsLoading,
  fallbackMessage,
  onSuccess,
  onSettled,
}: SubmitAuthFormOptions<TFormData>) {
  event.preventDefault();
  setError('');

  const validationError = getValidationError(schema, formData);
  if (validationError) {
    setError(validationError);
    return;
  }

  setIsLoading(true);

  try {
    await submit();
    await onSuccess?.();
  } catch (error) {
    setError(getSubmissionErrorMessage(error, fallbackMessage));
  } finally {
    setIsLoading(false);
    await onSettled?.();
  }
}
