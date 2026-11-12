using System;
using UnityEngine;

namespace HawChow.GTFOKillFeed;

public sealed class CorpseVisualLease : MonoBehaviour
{
	private Renderer[] _renderers;

	private bool[] _enabled;

	private bool[] _forceRenderingOff;

	private Rigidbody[] _bodies;

	private bool[] _kinematic;

	private bool[] _gravity;

	private bool[] _detectCollisions;

	private float[] _drag;

	private CollisionDetectionMode[] _detection;

	private Collider[] _colliders;

	private bool[] _colliderEnabled;

	internal Vector3[] Origins;

	internal int Generation;

	internal int BodyCount
	{
		get
		{
			Rigidbody[] bodies = _bodies;
			if (bodies == null)
			{
				return 0;
			}
			return bodies.Length;
		}
	}

	internal int RendererCount
	{
		get
		{
			Renderer[] renderers = _renderers;
			if (renderers == null)
			{
				return 0;
			}
			return renderers.Length;
		}
	}

	internal int ColliderCount
	{
		get
		{
			Collider[] colliders = _colliders;
			if (colliders == null)
			{
				return 0;
			}
			return colliders.Length;
		}
	}

	internal Vector3 Center
	{
		get
		{
			if (_bodies != null && _bodies.Length != 0 && _bodies[0] != null)
			{
				return _bodies[0].position;
			}
			return base.transform.position;
		}
	}

	public CorpseVisualLease(System.IntPtr pointer)
		: base(pointer)
	{
	}

	internal void Capture()
	{
		RestoreState();
		_renderers = GetComponentsInChildren<Renderer>(includeInactive: true);
		_enabled = new bool[_renderers.Length];
		_forceRenderingOff = new bool[_renderers.Length];
		for (int i = 0; i < _renderers.Length; i++)
		{
			_enabled[i] = _renderers[i].enabled;
			_forceRenderingOff[i] = _renderers[i].forceRenderingOff;
		}
		_bodies = GetComponentsInChildren<Rigidbody>(includeInactive: true);
		Origins = new Vector3[_bodies.Length];
		_kinematic = new bool[_bodies.Length];
		_gravity = new bool[_bodies.Length];
		_detectCollisions = new bool[_bodies.Length];
		_drag = new float[_bodies.Length];
		_detection = new CollisionDetectionMode[_bodies.Length];
		for (int j = 0; j < _bodies.Length; j++)
		{
			Rigidbody rigidbody = _bodies[j];
			Origins[j] = rigidbody.position;
			_kinematic[j] = rigidbody.isKinematic;
			_gravity[j] = rigidbody.useGravity;
			_detectCollisions[j] = rigidbody.detectCollisions;
			_drag[j] = rigidbody.drag;
			_detection[j] = rigidbody.collisionDetectionMode;
		}
		_colliders = GetComponentsInChildren<Collider>(includeInactive: true);
		_colliderEnabled = new bool[_colliders.Length];
		for (int k = 0; k < _colliders.Length; k++)
		{
			_colliderEnabled[k] = _colliders[k].enabled;
		}
	}

	internal void Launch(Vector3 direction, float strength, float speed)
	{
		if (_bodies == null || _bodies.Length == 0)
		{
			return;
		}
		float num = ((speed > 0f) ? speed : 0f);
		float num2 = FeedbackPolicy.LaunchUpSpeed(strength);
		bool flag = FeedbackPolicy.NeedsContinuousCollision(num);
		Vector3 center = Center;
		for (int i = 0; i < _bodies.Length; i++)
		{
			Rigidbody rigidbody = _bodies[i];
			if (!(rigidbody == null))
			{
				rigidbody.isKinematic = false;
				rigidbody.useGravity = true;
				rigidbody.drag = 0.8f;
				rigidbody.collisionDetectionMode = (flag ? CollisionDetectionMode.ContinuousDynamic : CapturedDetection(i));
				Vector3 vector = rigidbody.position - center;
				if (vector.sqrMagnitude < 0.01f)
				{
					vector = direction;
				}
				else
				{
					vector.y = Mathf.Max(vector.y, 0.15f);
					vector = (vector.normalized * 0.8f + direction * 0.2f).normalized;
				}
				rigidbody.velocity = vector * num + Vector3.up * num2;
				rigidbody.angularVelocity = Vector3.Cross(vector, Vector3.up) * (1f + strength * 2f);
			}
		}
	}

	private CollisionDetectionMode CapturedDetection(int index)
	{
		if (_detection == null || index < 0 || index >= _detection.Length)
		{
			return CollisionDetectionMode.Discrete;
		}
		return _detection[index];
	}

	internal void Limit(float maxDistance, float maxSpeed)
	{
		if (_bodies == null)
		{
			return;
		}
		float num = ((maxSpeed > 0f) ? maxSpeed : 9f);
		for (int i = 0; i < _bodies.Length; i++)
		{
			Rigidbody rigidbody = _bodies[i];
			if (!(rigidbody == null))
			{
				Vector3 vector = rigidbody.position - Origins[i];
				if (FeedbackPolicy.ShouldClampTravel(vector.sqrMagnitude, maxDistance))
				{
					rigidbody.position = Origins[i] + vector.normalized * maxDistance;
					rigidbody.velocity = Vector3.zero;
					rigidbody.angularVelocity = Vector3.zero;
				}
				if (rigidbody.velocity.sqrMagnitude > num * num)
				{
					rigidbody.velocity = rigidbody.velocity.normalized * num;
				}
			}
		}
	}

	internal Renderer PrimaryRenderer()
	{
		if (_renderers == null)
		{
			return null;
		}
		Renderer renderer = null;
		for (int i = 0; i < _renderers.Length; i++)
		{
			Renderer renderer2 = _renderers[i];
			try
			{
				if (!(renderer2 == null) && renderer2.enabled)
				{
					if (renderer2 is SkinnedMeshRenderer)
					{
						return renderer2;
					}
					if (renderer == null)
					{
						renderer = renderer2;
					}
				}
			}
			catch
			{
			}
		}
		return renderer;
	}

	internal void Hide()
	{
		if (_renderers == null)
		{
			return;
		}
		Renderer[] renderers = _renderers;
		foreach (Renderer renderer in renderers)
		{
			if (renderer != null)
			{
				renderer.forceRenderingOff = true;
				renderer.enabled = false;
			}
		}
	}

	internal void RecyclePhysics()
	{
		if (_bodies != null)
		{
			for (int i = 0; i < _bodies.Length; i++)
			{
				try
				{
					Rigidbody rigidbody = _bodies[i];
					if (!(rigidbody == null))
					{
						rigidbody.velocity = Vector3.zero;
						rigidbody.angularVelocity = Vector3.zero;
						rigidbody.isKinematic = true;
						rigidbody.detectCollisions = false;
						rigidbody.Sleep();
					}
				}
				catch
				{
				}
			}
		}
		if (_colliders == null)
		{
			return;
		}
		for (int j = 0; j < _colliders.Length; j++)
		{
			try
			{
				if (_colliders[j] != null)
				{
					_colliders[j].enabled = false;
				}
			}
			catch
			{
			}
		}
	}

	internal void RestoreState()
	{
		if (_renderers != null)
		{
			for (int i = 0; i < _renderers.Length; i++)
			{
				try
				{
					if (_renderers[i] != null)
					{
						_renderers[i].enabled = _enabled[i];
						_renderers[i].forceRenderingOff = _forceRenderingOff[i];
					}
				}
				catch
				{
				}
			}
		}
		if (_bodies != null)
		{
			for (int j = 0; j < _bodies.Length; j++)
			{
				try
				{
					Rigidbody rigidbody = _bodies[j];
					if (!(rigidbody == null))
					{
						rigidbody.velocity = Vector3.zero;
						rigidbody.angularVelocity = Vector3.zero;
						rigidbody.isKinematic = _kinematic[j];
						rigidbody.useGravity = _gravity[j];
						rigidbody.drag = _drag[j];
						rigidbody.detectCollisions = _detectCollisions[j];
						rigidbody.collisionDetectionMode = CapturedDetection(j);
					}
				}
				catch
				{
				}
			}
		}
		if (_colliders != null)
		{
			for (int k = 0; k < _colliders.Length; k++)
			{
				try
				{
					if (_colliders[k] != null)
					{
						_colliders[k].enabled = _colliderEnabled[k];
					}
				}
				catch
				{
				}
			}
		}
		_renderers = null;
		_enabled = null;
		_forceRenderingOff = null;
		_bodies = null;
		_kinematic = null;
		_gravity = null;
		_detectCollisions = null;
		_drag = null;
		_detection = null;
		_colliders = null;
		_colliderEnabled = null;
		Origins = null;
		Generation = ((Generation == 2147483647) ? 1 : (Generation + 1));
	}

	private void OnDisable()
	{
		RestoreState();
	}
}
