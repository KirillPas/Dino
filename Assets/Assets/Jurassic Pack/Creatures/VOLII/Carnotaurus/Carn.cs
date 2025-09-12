using UnityEngine;

public class Carn : Creature
{
	public Transform Spine0,Spine1,Spine2,Neck0,Neck1,Neck2,Tail2,Tail3,Tail4,Tail5,Tail6,Left_Hips,Right_Hips,Left_Leg,Right_Leg,Left_Foot0,Right_Foot0;
  public AudioClip Waterflush,Hit_jaw,Hit_head,Hit_tail,Bigstep,Largesplash,Largestep,Idlecarn,Bite,Swallow,Sniff1,Carn1,Carn2,Carn3,Carn4;

	//*************************************************************************************************************************************************
	//Play sound
	void OnCollisionStay(Collision col)
	{
		int rndPainsnd=Random.Range(0, 3); AudioClip painSnd=null;
		switch (rndPainsnd) { case 0: painSnd=Carn1; break; case 1: painSnd=Carn3; break; case 2: painSnd=Carn4; break; }
		ManageCollision(col, source, painSnd, Hit_jaw, Hit_head, Hit_tail);
	 }
	void PlaySound(string name, int time)
	{
		if(time==currframe && lastframe!=currframe)
		{
			switch (name)
			{
			case "Step": source[1].pitch=Random.Range(0.75f, 1.25f);
				if(isInWater) source[1].PlayOneShot(Waterflush, Random.Range(0.25f, 0.5f));
				else if(isOnWater) source[1].PlayOneShot(Largesplash, Random.Range(0.25f, 0.5f));
				else if(isOnGround) source[1].PlayOneShot(Bigstep, Random.Range(0.25f, 0.5f));
				lastframe=currframe; break;
			case "Bite": source[1].pitch=Random.Range(0.5f, 0.75f); source[1].PlayOneShot(Bite, 2.0f);
				lastframe=currframe; break;
			case "Die": source[1].pitch=Random.Range(1.0f, 1.25f); source[1].PlayOneShot(isOnWater|isInWater?Largesplash:Largestep, 1.0f);
				lastframe=currframe; isDead=true; break; 
			case "Food": source[0].pitch=Random.Range(1.0f, 1.25f); source[0].PlayOneShot(Swallow, 0.5f);
				lastframe=currframe; break;
			case "Sniff": source[0].pitch=Random.Range(1.0f, 1.25f); source[0].PlayOneShot(Sniff1, 0.5f);
				lastframe=currframe; break;
			case "Repose": source[0].pitch=Random.Range(0.75f, 1.25f); source[0].PlayOneShot(Idlecarn, 0.25f);
				lastframe=currframe; break;
			case "Atk": int rnd1=Random.Range (0, 2); source[0].pitch=Random.Range(0.75f, 1.75f);
				if(rnd1==0) source[0].PlayOneShot(Carn3, 0.5f);
				else source[0].PlayOneShot(Carn4, 0.5f);
				lastframe=currframe; break;
			case "Growl": int rnd2=Random.Range (0, 2); source[0].pitch=Random.Range(1.0f, 1.25f);
				if(rnd2==0)source[0].PlayOneShot(Carn1, 1.0f);
				else source[0].PlayOneShot(Carn2, 1.0f);
				lastframe=currframe; break;
			}
		}
	}
	
	//*************************************************************************************************************************************************
	// Add forces to the Rigidbody
	void FixedUpdate ()
	{
		StatusUpdate(); if(!isActive | animSpeed==0.0f) { body.Sleep(); return; }
		onReset=false; onAttack=false; isConstrained= false;

		if(useAI && health!=0) { AICore(1, 2, 3, 4, 5, 6, 7); }// CPU
		else if(health!=0) { GetUserInputs(1, 2, 3, 4, 5, 6, 7); }// Human
		else { anm.SetBool("Attack", false); anm.SetInteger ("Move", 0); anm.SetInteger ("Idle", -1); }//Dead

    //Set Y position
    if(isOnGround | isInWater | isOnWater)
    {
      if(!isOnGround && !isInWater) { body.linearDamping=1; body.angularDamping=1; } else { body.linearDamping=4; body.angularDamping=4; }
      ApplyYPos();
    } else ApplyGravity();

		//Stopped
		if(OnAnm.IsName("Carn|Idle1A") | OnAnm.IsName("Carn|Idle2A") |
			OnAnm.IsName("Carn|Die1") | OnAnm.IsName("Carn|Die2"))
		{
      Move(Vector3.zero);
			if(OnAnm.IsName("Carn|Die1")) { onReset=true; if(!isDead) { PlaySound("Atk", 2); PlaySound("Die", 12); } }
			else if(OnAnm.IsName("Carn|Die2")) { onReset=true; if(!isDead) { PlaySound("Atk", 2); PlaySound("Die", 10); } }
		}

		//End Forward
		else if(OnAnm.normalizedTime > 0.5 && (OnAnm.IsName("Carn|Step1+")| OnAnm.IsName("Carn|Step2+") |
		        OnAnm.IsName("Carn|ToIdle1C") | OnAnm.IsName("Carn|ToIdle2B") | OnAnm.IsName("Carn|ToIdle2D") | OnAnm.IsName("Carn|ToEatA") |
		        OnAnm.IsName("Carn|ToEatC") | OnAnm.IsName("Carn|StepAtk1") | OnAnm.IsName("Carn|StepAtk2")))
			PlaySound("Step", 9);

		//Forward
		else if(OnAnm.IsName("Carn|Walk") | OnAnm.IsName("Carn|WalkGrowl") | (OnAnm.normalizedTime < 0.5 &&
		   (OnAnm.IsName("Carn|Step1+") | OnAnm.IsName("Carn|Step2+") | OnAnm.IsName("Carn|ToIdle2B") |
		   OnAnm.IsName("Carn|ToIdle1C") | OnAnm.IsName("Carn|ToIdle2D") | OnAnm.IsName("Carn|ToEatA") | OnAnm.IsName("Carn|ToEatC")) ) )
		{
      Move(transform.forward, 50);
			if(OnAnm.IsName("Carn|WalkGrowl")) { PlaySound("Growl", 1); PlaySound("Step", 6); PlaySound("Step", 13); }
			else if(OnAnm.IsName("Carn|Walk")) { PlaySound("Step", 6); PlaySound("Step", 13); }
			else { PlaySound("Step", 8); PlaySound("Step", 12); }
		}

		//Run
		else if(OnAnm.IsName("Carn|Run") | OnAnm.IsName("Carn|RunGrowl") | OnAnm.IsName("Carn|WalkAtk1") | OnAnm.IsName("Carn|WalkAtk2") |
		   (OnAnm.normalizedTime < 0.6 && (OnAnm.IsName("Carn|StepAtk1") | OnAnm.IsName("Carn|StepAtk2"))))
		{
      roll=Mathf.Clamp(Mathf.Lerp(roll, spineX*5.0f, 0.05f), -20f, 20f);
			Move(transform.forward, 128);
			if(OnAnm.IsName("Carn|RunGrowl")) { PlaySound("Growl", 1); PlaySound("Step", 6); PlaySound("Step", 13); }
			else if(OnAnm.IsName("Carn|Run")) { PlaySound("Step", 6); PlaySound("Step", 13); }
			else if(OnAnm.IsName("Carn|StepAtk1") | OnAnm.IsName("Carn|StepAtk2")) { onAttack=true; PlaySound("Atk", 2); PlaySound("Bite", 5); }
			else { onAttack=true; PlaySound("Atk", 2); PlaySound("Step", 6); PlaySound("Bite", 9); PlaySound("Step", 13); }
		}

		//Backward
		else if((OnAnm.normalizedTime > 0.4 && OnAnm.normalizedTime < 0.8) && (OnAnm.IsName("Carn|Step1-") | OnAnm.IsName("Carn|Step2-") | OnAnm.IsName("Carn|ToSleep2")))
		{
			Move(-transform.forward, 50);
			PlaySound("Step", 12);
		}

		//Strafe/Turn right
		else if(OnAnm.IsName("Carn|Strafe1-") | OnAnm.IsName("Carn|Strafe2+"))
		{
			Move(transform.right, 25);
			PlaySound("Step", 6); PlaySound("Step", 13);
		}

		//Strafe/Turn left
		else if(OnAnm.IsName("Carn|Strafe1+") | OnAnm.IsName("Carn|Strafe2-"))
		{
			Move(-transform.right, 25);
			PlaySound("Step", 6); PlaySound("Step", 13);
		}

    //Idle Attack
    else if(OnAnm.IsName("Carn|IdleAtk1") | OnAnm.IsName("Carn|IdleAtk2"))
		{ 
      onAttack=true; Move(Vector3.zero);
      PlaySound("Atk", 1); PlaySound("Step", 3); PlaySound("Bite", 6);
    } 

		//Various
		else if(OnAnm.IsName("Carn|EatA")) { onReset=true; isConstrained=true; PlaySound("Food", 4); PlaySound("Bite", 5); }
		else if(OnAnm.IsName("Carn|EatB") | OnAnm.IsName("Carn|EatC")) { onReset=true; isConstrained=true; }
		else if(OnAnm.IsName("Carn|Sleep")) { onReset=true; isConstrained=true; PlaySound("Repose", 2); }
		else if(OnAnm.IsName("Carn|ToSleep1") | OnAnm.IsName("Carn|ToSleep2")) { onReset=true; isConstrained=true; }
		else if(OnAnm.IsName("Carn|ToIdle2A")) PlaySound("Sniff", 1);
		else if(OnAnm.IsName("Carn|Idle1B")) PlaySound("Growl", 2);
		else if(OnAnm.IsName("Carn|Idle1C")) { PlaySound("Sniff", 4); PlaySound("Sniff", 7); PlaySound("Sniff", 10);}
		else if(OnAnm.IsName("Carn|Idle2B")) { onReset=true; PlaySound("Bite", 4); PlaySound("Bite", 6); PlaySound("Bite", 8);}
		else if(OnAnm.IsName("Carn|Idle2C")) PlaySound("Growl", 2);
		else if(OnAnm.IsName("Carn|Idle2D")) { onReset=true; PlaySound("Atk", 2); }
		else if(OnAnm.IsName("Carn|Die1-")) { isConstrained=true; PlaySound("Growl", 3);  isDead=false; }
		else if(OnAnm.IsName("Carn|Die2-")) { isConstrained=true; PlaySound("Growl", 3);  isDead=false; }

    RotateBone(IkType.LgBiped, 65f);
	}
	
  //*************************************************************************************************************************************************
	// Bone rotation
	void LateUpdate()
	{
		if(!isActive) return; headPos=Head.GetChild(0).GetChild(0).position;

		Spine0.rotation*= Quaternion.AngleAxis(headX, Vector3.forward)*Quaternion.AngleAxis(-headY, Vector3.right);
		Spine2.rotation*= Quaternion.AngleAxis(headX, Vector3.forward)*Quaternion.AngleAxis(-headY, Vector3.right);
		Neck0.rotation*= Quaternion.AngleAxis(headX, Vector3.forward)*Quaternion.AngleAxis(-headY, Vector3.right);
		Neck1.rotation*= Quaternion.AngleAxis(headX, Vector3.forward)*Quaternion.AngleAxis(-headY, Vector3.right);
		Neck2.rotation*= Quaternion.AngleAxis(headX, Vector3.forward)*Quaternion.AngleAxis(-headY, Vector3.right);
		Head.rotation*= Quaternion.AngleAxis(headX, Vector3.forward)*Quaternion.AngleAxis(-headY, Vector3.right);
		Tail2.rotation*= Quaternion.AngleAxis(-spineX, Vector3.forward);
		Tail3.rotation*= Quaternion.AngleAxis(-spineX, Vector3.forward);
		Tail4.rotation*= Quaternion.AngleAxis(-spineX, Vector3.forward);
		Tail5.rotation*= Quaternion.AngleAxis(-spineX, Vector3.forward);
		Tail6.rotation*= Quaternion.AngleAxis(-spineX, Vector3.forward);

		Right_Hips.rotation*= Quaternion.Euler(-roll, 0, 0);
		Left_Hips.rotation*= Quaternion.Euler(-roll, 0, 0);
    if(!isDead) Head.GetChild(0).transform.rotation*=Quaternion.Euler(lastHit, 0, 0);
		//Check for ground layer
		GetGroundPos(IkType.LgBiped, Right_Hips, Right_Leg, Right_Foot0, Left_Hips, Left_Leg, Left_Foot0);
	}
}

